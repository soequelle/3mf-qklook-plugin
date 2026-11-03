using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QuickLook.Plugin.ThreeMFViewer.ThreeMF;

/// <summary>
/// Slicer specific project data stored next to the 3MF model: filament colours, which
/// filament each object/part uses, and which parts are modifiers that are not printed.
/// </summary>
internal sealed class SlicerConfig
{
    // Bambu Studio / OrcaSlicer
    private const string BambuProjectSettings = "Metadata/project_settings.config";
    private const string BambuModelSettings = "Metadata/model_settings.config";

    // PrusaSlicer (and SuperSlicer)
    private const string PrusaProjectSettings = "Metadata/Slic3r_PE.config";
    private const string PrusaModelSettings = "Metadata/Slic3r_PE_model.config";

    private readonly Dictionary<int, int> _objectFilaments = [];
    private readonly Dictionary<long, PartInfo> _parts = [];
    private readonly Dictionary<int, List<VolumeRange>> _volumes = [];

    public List<uint> FilamentColors { get; private set; } = [];

    public static SlicerConfig Load(ZipArchive zip)
    {
        var config = new SlicerConfig();

        try
        {
            config.LoadBambu(zip);
            config.LoadPrusa(zip);
        }
        catch (Exception)
        {
            // Colours are a nice to have: never fail the preview because of them.
        }

        return config;
    }

    /// <summary>Filament set on a top-level object of the main model file, or 0.</summary>
    public int GetObjectFilament(int objectId) =>
        _objectFilaments.TryGetValue(objectId, out var filament) ? filament : 0;

    /// <summary>Bambu/Orca part of a top-level object (the component's object id).</summary>
    public PartInfo GetPart(int objectId, int partId) =>
        _parts.TryGetValue(PartKey(objectId, partId), out var part) ? part : null;

    /// <summary>PrusaSlicer volumes (triangle ranges) of a top-level object.</summary>
    public List<VolumeRange> GetVolumes(int objectId) =>
        _volumes.TryGetValue(objectId, out var volumes) ? volumes : null;

    private static long PartKey(int objectId, int partId) => ((long)objectId << 32) | (uint)partId;

    private void LoadBambu(ZipArchive zip)
    {
        var settings = ReadText(zip, BambuProjectSettings);
        if (settings != null)
        {
            var list = Regex.Match(settings, "\"filament_colou?r\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (list.Success)
            {
                FilamentColors = Regex.Matches(list.Groups[1].Value, "\"([^\"]*)\"")
                    .Cast<Match>()
                    .Select(m => ParseColor(m.Groups[1].Value) ?? 0)
                    .ToList();
            }
        }

        var model = ReadXml(zip, BambuModelSettings);
        if (model == null)
            return;

        foreach (var obj in model.Root.Elements("object"))
        {
            var objectId = ParseInt(obj.Attribute("id")?.Value);
            var objectFilament = ParseInt(MetadataValue(obj, "extruder"));
            if (objectFilament > 0)
                _objectFilaments[objectId] = objectFilament;

            foreach (var part in obj.Elements("part"))
            {
                var partId = ParseInt(part.Attribute("id")?.Value);
                var subtype = part.Attribute("subtype")?.Value;
                _parts[PartKey(objectId, partId)] = new PartInfo
                {
                    Filament = ParseInt(MetadataValue(part, "extruder")),
                    IsPrinted = string.IsNullOrEmpty(subtype) || subtype == "normal_part",
                };
            }
        }
    }

    private void LoadPrusa(ZipArchive zip)
    {
        var settings = ReadText(zip, PrusaProjectSettings);
        if (settings != null)
        {
            var extruderColors = ReadIniList(settings, "extruder_colour");
            var filamentColors = ReadIniList(settings, "filament_colour");
            var count = Math.Max(extruderColors.Count, filamentColors.Count);

            // PrusaSlicer shows the extruder colour when set, the filament colour otherwise.
            FilamentColors = Enumerable.Range(0, count)
                .Select(i => (i < extruderColors.Count ? ParseColor(extruderColors[i]) : null)
                             ?? (i < filamentColors.Count ? ParseColor(filamentColors[i]) : null)
                             ?? 0)
                .ToList();
        }

        var model = ReadXml(zip, PrusaModelSettings);
        if (model == null)
            return;

        foreach (var obj in model.Root.Elements("object"))
        {
            var objectId = ParseInt(obj.Attribute("id")?.Value);
            var objectFilament = ParseInt(MetadataValue(obj, "extruder"));
            if (objectFilament > 0)
                _objectFilaments[objectId] = objectFilament;

            var volumes = new List<VolumeRange>();
            foreach (var volume in obj.Elements("volume"))
            {
                var type = MetadataValue(volume, "volume_type");
                var isModifier = MetadataValue(volume, "modifier") == "1";
                volumes.Add(new VolumeRange
                {
                    FirstTriangle = ParseInt(volume.Attribute("firstid")?.Value),
                    LastTriangle = ParseInt(volume.Attribute("lastid")?.Value),
                    Filament = ParseInt(MetadataValue(volume, "extruder")),
                    IsPrinted = type == null ? !isModifier : type == "ModelPart",
                });
            }

            if (volumes.Count > 0)
                _volumes[objectId] = volumes;
        }
    }

    private static string MetadataValue(XElement element, string key) =>
        element.Elements("metadata").FirstOrDefault(m => (string)m.Attribute("key") == key)?.Attribute("value")?.Value;

    private static List<string> ReadIniList(string ini, string key)
    {
        var match = Regex.Match(ini, "^;?\\s*" + key + "\\s*=\\s*(.*)$", RegexOptions.Multiline);
        if (!match.Success)
            return [];

        return match.Groups[1].Value.Trim()
            .Split(';')
            .Select(s => s.Trim().Trim('"'))
            .ToList();
    }

    /// <summary>Parses #RRGGBB or #RRGGBBAA into ARGB.</summary>
    public static uint? ParseColor(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '#')
            return null;

        var hex = value.Substring(1);
        if (hex.Length != 6 && hex.Length != 8)
            return null;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
            return null;

        return hex.Length == 6
            ? 0xFF000000 | rgba
            : (rgba << 24) | (rgba >> 8);
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static string ReadText(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry == null)
            return null;

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static XDocument ReadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry == null)
            return null;

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}

internal sealed class PartInfo
{
    public int Filament { get; set; }

    public bool IsPrinted { get; set; } = true;
}

internal sealed class VolumeRange
{
    public int FirstTriangle { get; set; }

    public int LastTriangle { get; set; }

    public int Filament { get; set; }

    public bool IsPrinted { get; set; } = true;
}
