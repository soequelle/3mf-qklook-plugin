using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace QuickLook.Plugin.ThreeMFViewer.ThreeMF;

/// <summary>
/// What colour a triangle should be drawn with: either a filament slot of the slicer
/// (0 = whatever the part/object uses) or an explicit ARGB colour from the 3MF materials.
/// </summary>
internal readonly struct ColorKey : IEquatable<ColorKey>
{
    private ColorKey(int filament, uint argb, bool isExplicit)
    {
        Filament = filament;
        Argb = argb;
        IsExplicit = isExplicit;
    }

    public int Filament { get; }

    public uint Argb { get; }

    public bool IsExplicit { get; }

    public static ColorKey Inherit => default;

    public static ColorKey FromFilament(int filament) => new(filament, 0, false);

    public static ColorKey FromColor(uint argb) => new(0, argb, true);

    public bool Equals(ColorKey other) =>
        Filament == other.Filament && Argb == other.Argb && IsExplicit == other.IsExplicit;

    public override bool Equals(object obj) => obj is ColorKey other && Equals(other);

    public override int GetHashCode() => IsExplicit ? (int)Argb : Filament;
}

/// <summary>
/// A triangle mesh as stored in a 3MF object, with every triangle tagged by colour.
/// </summary>
internal sealed class MeshData
{
    /// <summary>x, y, z per vertex.</summary>
    public float[] Vertices { get; set; }

    /// <summary>v1, v2, v3 per triangle.</summary>
    public int[] Triangles { get; set; }

    /// <summary>Index into <see cref="Keys"/> per triangle.</summary>
    public ushort[] TriangleKeys { get; set; }

    public List<ColorKey> Keys { get; } = [];

    public int TriangleCount => Triangles.Length / 3;
}

internal sealed class Component
{
    /// <summary>Package path of the model file holding the object, or null for the same file.</summary>
    public string Path { get; set; }

    public int ObjectId { get; set; }

    public Matrix3D Transform { get; set; } = Matrix3D.Identity;
}

internal sealed class ObjectResource
{
    public int Id { get; set; }

    public string Name { get; set; }

    /// <summary>Default material group (<c>pid</c>) and index (<c>pindex</c>) of the triangles.</summary>
    public string Pid { get; set; }

    public string PIndex { get; set; }

    public MeshData Mesh { get; set; }

    public List<Component> Components { get; } = [];
}

internal sealed class BuildItem
{
    public int ObjectId { get; set; }

    public string Path { get; set; }

    public Matrix3D Transform { get; set; } = Matrix3D.Identity;
}

/// <summary>
/// One mesh placed in the scene. Shared meshes (the same object used by several
/// components or build items) appear several times with the same <see cref="Mesh"/>.
/// </summary>
internal sealed class MeshInstance
{
    public MeshData Mesh { get; set; }

    /// <summary>Local mesh coordinates to scene millimetres.</summary>
    public Matrix3D Transform { get; set; }

    /// <summary>Filament used by triangles tagged <see cref="ColorKey.Inherit"/>.</summary>
    public int DefaultFilament { get; set; }
}

internal sealed class ThreeMFScene
{
    public List<MeshInstance> Instances { get; } = [];

    /// <summary>Filament colours as ARGB, index 0 being filament 1.</summary>
    public List<uint> FilamentColors { get; set; } = [];

    public string Application { get; set; }

    public int ObjectCount { get; set; }

    public long TriangleCount { get; set; }
}
