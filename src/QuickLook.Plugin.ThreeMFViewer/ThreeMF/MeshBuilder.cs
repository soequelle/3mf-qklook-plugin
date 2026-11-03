using System;
using System.Collections.Generic;

namespace QuickLook.Plugin.ThreeMFViewer.ThreeMF;

/// <summary>
/// Accumulates the vertices and triangles of one &lt;mesh&gt; and works out the colour of
/// every triangle, in this order: multi-material painting, PrusaSlicer volume, 3MF material.
/// </summary>
internal sealed class MeshBuilder
{
    public const uint NeutralColor = 0xFFB4B4B4;

    private readonly Dictionary<int, List<uint>> _materials;
    private readonly int _objectPid;
    private readonly int _objectPIndex;
    private readonly List<VolumeRange> _volumes;
    private readonly Dictionary<ColorKey, ushort> _keyIndices = [];
    private readonly MeshData _mesh = new();

    private float[] _vertices = new float[3 * 1024];
    private int _vertexFloats;
    private int[] _triangles = new int[3 * 1024];
    private ushort[] _triangleKeys = new ushort[1024];
    private int _triangleCount;
    private int _sourceTriangle;
    private int _volumeIndex;

    public MeshBuilder(Dictionary<int, List<uint>> materials, int objectPid, int objectPIndex, List<VolumeRange> volumes)
    {
        _materials = materials;
        _objectPid = objectPid;
        _objectPIndex = objectPIndex;
        _volumes = volumes;
    }

    public void AddVertex(double x, double y, double z)
    {
        if (_vertexFloats + 3 > _vertices.Length)
            Array.Resize(ref _vertices, _vertices.Length * 2);

        _vertices[_vertexFloats++] = (float)x;
        _vertices[_vertexFloats++] = (float)y;
        _vertices[_vertexFloats++] = (float)z;
    }

    public void AddTriangle(int v1, int v2, int v3, string paint, string pid, string p1)
    {
        var volume = CurrentVolume(_sourceTriangle++);
        if (volume is { IsPrinted: false })
            return;

        ColorKey key;
        var state = PaintDecoder.DominantState(paint);
        if (state > 0)
            key = ColorKey.FromFilament(state);
        else if (volume is { Filament: > 0 })
            key = ColorKey.FromFilament(volume.Filament);
        else if (ResolveMaterial(pid, p1) is uint color)
            key = ColorKey.FromColor(color);
        else
            key = ColorKey.Inherit;

        if (_triangleCount == _triangleKeys.Length)
        {
            Array.Resize(ref _triangles, _triangles.Length * 2);
            Array.Resize(ref _triangleKeys, _triangleKeys.Length * 2);
        }

        var t = _triangleCount * 3;
        _triangles[t] = v1;
        _triangles[t + 1] = v2;
        _triangles[t + 2] = v3;
        _triangleKeys[_triangleCount] = KeyIndex(key);
        _triangleCount++;
    }

    public MeshData Build()
    {
        if (_vertexFloats == 0 || _triangleCount == 0)
            return null;

        // Drop triangles pointing at vertices that do not exist instead of failing later.
        var vertexCount = _vertexFloats / 3;
        var valid = 0;
        for (var i = 0; i < _triangleCount; i++)
        {
            int a = _triangles[i * 3], b = _triangles[i * 3 + 1], c = _triangles[i * 3 + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                continue;

            _triangles[valid * 3] = a;
            _triangles[valid * 3 + 1] = b;
            _triangles[valid * 3 + 2] = c;
            _triangleKeys[valid] = _triangleKeys[i];
            valid++;
        }

        Array.Resize(ref _vertices, _vertexFloats);
        Array.Resize(ref _triangles, valid * 3);
        Array.Resize(ref _triangleKeys, valid);

        _mesh.Vertices = _vertices;
        _mesh.Triangles = _triangles;
        _mesh.TriangleKeys = _triangleKeys;
        return _mesh;
    }

    private VolumeRange CurrentVolume(int triangle)
    {
        if (_volumes == null)
            return null;

        // Volumes are consecutive triangle ranges and triangles arrive in order.
        while (_volumeIndex < _volumes.Count && triangle > _volumes[_volumeIndex].LastTriangle)
            _volumeIndex++;

        if (_volumeIndex < _volumes.Count && triangle >= _volumes[_volumeIndex].FirstTriangle)
            return _volumes[_volumeIndex];

        return null;
    }

    private uint? ResolveMaterial(string pid, string p1)
    {
        var group = string.IsNullOrEmpty(pid) ? _objectPid : ParseInt(pid);
        if (group < 0 || _materials.Count == 0 || !_materials.TryGetValue(group, out var colors))
            return null;

        var index = string.IsNullOrEmpty(p1) ? _objectPIndex : ParseInt(p1);
        return index >= 0 && index < colors.Count ? colors[index] : null;
    }

    private ushort KeyIndex(ColorKey key)
    {
        if (_keyIndices.TryGetValue(key, out var index))
            return index;

        index = (ushort)Math.Min(_mesh.Keys.Count, ushort.MaxValue);
        if (index < ushort.MaxValue)
        {
            _mesh.Keys.Add(key);
            _keyIndices[key] = index;
        }

        return index == ushort.MaxValue ? (ushort)0 : index;
    }

    private static int ParseInt(string value) => int.TryParse(value, out var result) ? result : -1;
}
