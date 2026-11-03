using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media.Media3D;
using System.Xml;
using System.Xml.Linq;

namespace QuickLook.Plugin.ThreeMFViewer.ThreeMF;

/// <summary>
/// Reads the geometry of a 3MF package, following the components of the Production
/// extension (<c>p:path</c>) that Bambu Studio, OrcaSlicer and others use to store every
/// object in its own model file.
/// </summary>
internal sealed class ThreeMFReader
{
    private const string ProductionNs = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private const string ModelRelationship = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";
    private const string ThumbnailRelationship = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";
    private const int MaxDepth = 32;

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Dictionary<string, ModelFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly SlicerConfig _config;
    private readonly CancellationToken _cancellation;
    private string _mainPath;

    private ThreeMFReader(ZipArchive zip, CancellationToken cancellation)
    {
        _zip = zip;
        _cancellation = cancellation;
        _entries = zip.Entries
            .GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _config = SlicerConfig.Load(zip);
    }

    public static ThreeMFScene Read(string path, CancellationToken cancellation = default)
    {
        using var zip = OpenZip(path);
        return new ThreeMFReader(zip, cancellation).ReadScene();
    }

    /// <summary>
    /// Images embedded by the slicer: one render per plate (Bambu/Orca), or the package thumbnail.
    /// </summary>
    public static List<EmbeddedImage> ReadImages(string path)
    {
        using var zip = OpenZip(path);
        var images = new List<EmbeddedImage>();

        var plates = zip.Entries
            .Select(e => new { Entry = e, Match = Regex.Match(e.FullName, @"^Metadata/plate_(\d+)\.png$", RegexOptions.IgnoreCase) })
            .Where(p => p.Match.Success)
            .OrderBy(p => int.Parse(p.Match.Groups[1].Value))
            .ToList();
        foreach (var plate in plates)
            images.Add(new EmbeddedImage { Plate = int.Parse(plate.Match.Groups[1].Value), Data = ReadBytes(plate.Entry) });

        if (images.Count > 0)
            return images;

        var candidates = new List<string>();
        candidates.AddRange(ReadRelationships(zip, "_rels/.rels", ThumbnailRelationship));
        candidates.Add("Metadata/thumbnail.png");
        candidates.Add("Auxiliaries/.thumbnails/thumbnail_middle.png");
        candidates.Add("Auxiliaries/.thumbnails/thumbnail_3mf.png");

        foreach (var candidate in candidates)
        {
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, candidate, StringComparison.OrdinalIgnoreCase));
            if (entry != null && IsSupportedImage(entry.FullName))
            {
                images.Add(new EmbeddedImage { Data = ReadBytes(entry) });
                break;
            }
        }

        return images;
    }

    private static ZipArchive OpenZip(string path)
    {
        // Slicers keep the project open: share everything so the preview never fails because of it.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private ThreeMFScene ReadScene()
    {
        _mainPath = ReadRelationships(_zip, "_rels/.rels", ModelRelationship).FirstOrDefault(_entries.ContainsKey)
                    ?? (_entries.ContainsKey("3D/3dmodel.model") ? "3D/3dmodel.model" : null)
                    ?? _entries.Keys.FirstOrDefault(k => k.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("The package has no 3D model.");

        var main = GetFile(_mainPath);
        var scene = new ThreeMFScene
        {
            Application = main.Application,
            FilamentColors = _config.FilamentColors,
            ObjectCount = main.Build.Count,
        };

        var unit = new Matrix3D();
        unit.Scale(new Vector3D(main.UnitScale, main.UnitScale, main.UnitScale));

        foreach (var item in main.Build)
        {
            var file = item.Path == null ? main : GetFile(item.Path);
            var topObjectId = file == main ? item.ObjectId : -1;
            Visit(scene, file, item.ObjectId, item.Transform * unit, topObjectId, null, 0);
        }

        scene.TriangleCount = scene.Instances.Sum(i => (long)i.Mesh.TriangleCount);
        return scene;
    }

    private void Visit(ThreeMFScene scene, ModelFile file, int objectId, Matrix3D transform, int topObjectId, PartInfo part, int depth)
    {
        if (depth > MaxDepth || file == null || !file.Objects.TryGetValue(objectId, out var obj))
            return;

        if (obj.Mesh != null && obj.Mesh.TriangleCount > 0)
        {
            var filament = part?.Filament ?? 0;
            if (filament <= 0)
                filament = _config.GetObjectFilament(topObjectId);

            scene.Instances.Add(new MeshInstance
            {
                Mesh = obj.Mesh,
                Transform = transform,
                DefaultFilament = filament > 0 ? filament : 1,
            });
        }

        foreach (var component in obj.Components)
        {
            var childPart = part;
            if (depth == 0)
            {
                // Bambu/Orca: the components of a top-level object are its parts.
                var info = _config.GetPart(topObjectId, component.ObjectId);
                if (info != null)
                {
                    if (!info.IsPrinted)
                        continue;
                    childPart = info;
                }
            }

            var childFile = component.Path == null ? file : GetFile(component.Path);
            Visit(scene, childFile, component.ObjectId, component.Transform * transform, topObjectId, childPart, depth + 1);
        }
    }

    private ModelFile GetFile(string path)
    {
        path = NormalizePath(path);
        if (_files.TryGetValue(path, out var file))
            return file;

        // Register before parsing so a file referencing itself cannot recurse forever.
        _files[path] = null;
        if (_entries.TryGetValue(path, out var entry))
            _files[path] = file = ParseModel(entry, path.Equals(_mainPath, StringComparison.OrdinalIgnoreCase));

        return file;
    }

    private ModelFile ParseModel(ZipArchiveEntry entry, bool isMain)
    {
        var file = new ModelFile();
        var materials = new Dictionary<int, List<uint>>();
        List<uint> currentMaterials = null;
        ObjectResource current = null;
        MeshBuilder mesh = null;
        var readingApplication = false;
        var elementCount = 0;

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Text && readingApplication)
            {
                file.Application = reader.Value.Trim();
                readingApplication = false;
                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "object" when current != null:
                        current.Mesh = mesh?.Build();
                        file.Objects[current.Id] = current;
                        current = null;
                        mesh = null;
                        break;

                    case "basematerials":
                    case "colorgroup":
                        currentMaterials = null;
                        break;

                    case "metadata":
                        readingApplication = false;
                        break;
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (++elementCount % 4096 == 0)
                _cancellation.ThrowIfCancellationRequested();

            switch (reader.LocalName)
            {
                case "vertex" when mesh != null:
                    mesh.AddVertex(
                        FastFloat.Parse(reader.GetAttribute("x")),
                        FastFloat.Parse(reader.GetAttribute("y")),
                        FastFloat.Parse(reader.GetAttribute("z")));
                    break;

                case "triangle" when mesh != null:
                    mesh.AddTriangle(
                        ParseInt(reader.GetAttribute("v1")),
                        ParseInt(reader.GetAttribute("v2")),
                        ParseInt(reader.GetAttribute("v3")),
                        reader.GetAttribute("paint_color") ?? reader.GetAttribute("slic3rpe:mmu_segmentation"),
                        reader.GetAttribute("pid"),
                        reader.GetAttribute("p1"));
                    break;

                case "mesh" when current != null:
                    mesh = new MeshBuilder(
                        materials,
                        ParseIntOrDefault(current.Pid, -1),
                        ParseIntOrDefault(current.PIndex, 0),
                        isMain ? _config.GetVolumes(current.Id) : null);
                    break;

                case "object":
                    current = new ObjectResource
                    {
                        Id = ParseInt(reader.GetAttribute("id")),
                        Name = reader.GetAttribute("name"),
                        Pid = reader.GetAttribute("pid"),
                        PIndex = reader.GetAttribute("pindex"),
                    };
                    break;

                case "component" when current != null:
                    current.Components.Add(new Component
                    {
                        ObjectId = ParseInt(reader.GetAttribute("objectid")),
                        Path = reader.GetAttribute("path", ProductionNs),
                        Transform = ParseTransform(reader.GetAttribute("transform")),
                    });
                    break;

                case "item":
                    file.Build.Add(new BuildItem
                    {
                        ObjectId = ParseInt(reader.GetAttribute("objectid")),
                        Path = reader.GetAttribute("path", ProductionNs),
                        Transform = ParseTransform(reader.GetAttribute("transform")),
                    });
                    break;

                case "basematerials":
                case "colorgroup":
                    currentMaterials = [];
                    materials[ParseInt(reader.GetAttribute("id"))] = currentMaterials;
                    break;

                case "base" when currentMaterials != null:
                    currentMaterials.Add(SlicerConfig.ParseColor(reader.GetAttribute("displaycolor")) ?? MeshBuilder.NeutralColor);
                    break;

                case "color" when currentMaterials != null:
                    currentMaterials.Add(SlicerConfig.ParseColor(reader.GetAttribute("color")) ?? MeshBuilder.NeutralColor);
                    break;

                case "model":
                    file.UnitScale = UnitToMillimetres(reader.GetAttribute("unit"));
                    break;

                case "metadata" when current == null && !reader.IsEmptyElement:
                    readingApplication = reader.GetAttribute("name") == "Application";
                    break;
            }
        }

        return file;
    }

    private static IEnumerable<string> ReadRelationships(ZipArchive zip, string relsPath, string type)
    {
        var entry = zip.GetEntry(relsPath);
        if (entry == null)
            return [];

        try
        {
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            return doc.Root.Elements()
                .Where(e => e.Name.LocalName == "Relationship" && (string)e.Attribute("Type") == type)
                .Select(e => NormalizePath((string)e.Attribute("Target")))
                .ToList();
        }
        catch (XmlException)
        {
            return [];
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        return Uri.UnescapeDataString(path).Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// Parses "m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32". 3MF uses row vectors,
    /// like WPF, so the values map one to one onto <see cref="Matrix3D"/>.
    /// </summary>
    private static Matrix3D ParseTransform(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Matrix3D.Identity;

        var m = value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(FastFloat.Parse)
            .ToArray();
        if (m.Length != 12)
            return Matrix3D.Identity;

        return new Matrix3D(
            m[0], m[1], m[2], 0,
            m[3], m[4], m[5], 0,
            m[6], m[7], m[8], 0,
            m[9], m[10], m[11], 1);
    }

    private static double UnitToMillimetres(string unit) => unit switch
    {
        "micron" => 0.001,
        "centimeter" => 10,
        "inch" => 25.4,
        "foot" => 304.8,
        "meter" => 1000,
        _ => 1,
    };

    private static int ParseInt(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        var result = 0;
        foreach (var c in value)
        {
            if (c < '0' || c > '9')
                return int.TryParse(value, out var parsed) ? parsed : 0;
            result = result * 10 + (c - '0');
        }

        return result;
    }

    private static int ParseIntOrDefault(string value, int fallback) =>
        string.IsNullOrEmpty(value) ? fallback : ParseInt(value);

    private static bool IsSupportedImage(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class ModelFile
    {
        public Dictionary<int, ObjectResource> Objects { get; } = [];

        public List<BuildItem> Build { get; } = [];

        public double UnitScale { get; set; } = 1;

        public string Application { get; set; }
    }
}

internal sealed class EmbeddedImage
{
    /// <summary>Plate number, or 0 for the package thumbnail.</summary>
    public int Plate { get; set; }

    public byte[] Data { get; set; }
}
