using QuickLook.Plugin.ThreeMFViewer.ThreeMF;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace QuickLook.Plugin.ThreeMFViewer.Rendering;

/// <summary>
/// Turns a <see cref="ThreeMFScene"/> into a frozen WPF model, so it can be built on a
/// background thread and handed to the UI thread.
/// </summary>
internal static class SceneBuilder
{
    // Used when the file does not say which colour a filament is.
    private static readonly uint[] FallbackPalette =
    [
        0xFFB4B4B4, 0xFFE8743B, 0xFF19A979, 0xFF5899DA, 0xFFED4A7B,
        0xFF945ECF, 0xFF13A4B4, 0xFFF4C340, 0xFF6C8893, 0xFFEE6868,
    ];

    public static Model3DGroup Build(ThreeMFScene scene, CancellationToken cancellation = default)
    {
        var root = new Model3DGroup();
        var geometries = new Dictionary<MeshData, List<KeyValuePair<ColorKey, MeshGeometry3D>>>();
        var materials = new Dictionary<uint, Material>();

        foreach (var instance in scene.Instances)
        {
            cancellation.ThrowIfCancellationRequested();

            if (!geometries.TryGetValue(instance.Mesh, out var parts))
                geometries[instance.Mesh] = parts = CreateGeometries(instance.Mesh);

            var group = new Model3DGroup { Transform = new MatrixTransform3D(instance.Transform) };
            foreach (var part in parts)
            {
                var argb = ResolveColor(part.Key, instance.DefaultFilament, scene.FilamentColors);
                if (!materials.TryGetValue(argb, out var material))
                    materials[argb] = material = CreateMaterial(argb);

                group.Children.Add(new GeometryModel3D(part.Value, material) { BackMaterial = material });
            }

            root.Children.Add(group);
        }

        root.Freeze();
        return root;
    }

    /// <summary>
    /// One geometry per colour. Vertices are not shared between triangles so that every
    /// face gets its own normal: flat shading, which is what slicers show.
    /// </summary>
    private static List<KeyValuePair<ColorKey, MeshGeometry3D>> CreateGeometries(MeshData mesh)
    {
        var counts = new int[mesh.Keys.Count];
        foreach (var key in mesh.TriangleKeys)
            counts[key]++;

        var result = new List<KeyValuePair<ColorKey, MeshGeometry3D>>();
        var v = mesh.Vertices;
        var t = mesh.Triangles;

        for (var k = 0; k < mesh.Keys.Count; k++)
        {
            if (counts[k] == 0)
                continue;

            var positions = new Point3DCollection(counts[k] * 3);
            var normals = new Vector3DCollection(counts[k] * 3);

            for (var i = 0; i < mesh.TriangleKeys.Length; i++)
            {
                if (mesh.TriangleKeys[i] != k)
                    continue;

                var a = Vertex(v, t[i * 3]);
                var b = Vertex(v, t[i * 3 + 1]);
                var c = Vertex(v, t[i * 3 + 2]);

                var normal = Vector3D.CrossProduct(b - a, c - a);
                if (normal.LengthSquared > 0)
                    normal.Normalize();
                else
                    normal = new Vector3D(0, 0, 1);

                positions.Add(a);
                positions.Add(b);
                positions.Add(c);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
            }

            var geometry = new MeshGeometry3D { Positions = positions, Normals = normals };
            geometry.Freeze();
            result.Add(new KeyValuePair<ColorKey, MeshGeometry3D>(mesh.Keys[k], geometry));
        }

        return result;
    }

    private static Point3D Vertex(float[] vertices, int index) =>
        new(vertices[index * 3], vertices[index * 3 + 1], vertices[index * 3 + 2]);

    private static uint ResolveColor(ColorKey key, int defaultFilament, List<uint> filamentColors)
    {
        // Transparency would need depth sorting, which WPF does not do: draw everything opaque.
        if (key.IsExplicit)
            return key.Argb | 0xFF000000;

        var filament = key.Filament > 0 ? key.Filament : defaultFilament;
        if (filament >= 1 && filament <= filamentColors.Count && filamentColors[filament - 1] != 0)
            return filamentColors[filament - 1] | 0xFF000000;

        return FallbackPalette[(System.Math.Max(filament, 1) - 1) % FallbackPalette.Length];
    }

    private static Material CreateMaterial(uint argb)
    {
        var color = Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), 40));
        material.Freeze();
        return material;
    }
}
