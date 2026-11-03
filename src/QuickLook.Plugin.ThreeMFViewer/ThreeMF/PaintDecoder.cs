using System;

namespace QuickLook.Plugin.ThreeMFViewer.ThreeMF;

/// <summary>
/// Decodes the per-triangle multi-material painting written by PrusaSlicer
/// (<c>slic3rpe:mmu_segmentation</c>), Bambu Studio and OrcaSlicer (<c>paint_color</c>).
/// </summary>
/// <remarks>
/// The value is a tree of subdivided triangles serialized as hex nibbles that are read
/// from the end of the string, in pre-order. Each nibble holds the number of split sides
/// in its two low bits (0 = leaf); for a leaf the two high bits hold the state, where 3
/// means "read the next nibble and add 3". The state is the 1-based filament number,
/// 0 meaning "not painted".
/// </remarks>
internal static class PaintDecoder
{
    private const int MaxState = 64;

    [ThreadStatic]
    private static double[] _weights;

    /// <summary>
    /// Returns the filament that covers most of the triangle, or 0 when it is mostly unpainted.
    /// </summary>
    public static int DominantState(string code)
    {
        if (string.IsNullOrEmpty(code))
            return 0;

        // Fast path: a single leaf, which is by far the most common case.
        var last = Nibble(code[code.Length - 1]);
        if ((last & 0b11) == 0)
        {
            var state = last >> 2;
            if (state != 3)
                return state;
            if (code.Length >= 2)
                return Nibble(code[code.Length - 2]) + 3;
        }

        var weights = _weights ??= new double[MaxState];
        Array.Clear(weights, 0, weights.Length);

        var pos = code.Length - 1;
        Walk(code, ref pos, 1d, weights, 0);

        var best = 0;
        for (var i = 1; i < weights.Length; i++)
        {
            if (weights[i] > weights[best])
                best = i;
        }

        return best;
    }

    private static void Walk(string code, ref int pos, double weight, double[] weights, int depth)
    {
        if (pos < 0 || depth > 32)
            return;

        var nibble = Nibble(code[pos--]);
        var splitSides = nibble & 0b11;

        if (splitSides == 0)
        {
            var state = nibble >> 2;
            if (state == 3)
            {
                if (pos < 0)
                    return;
                state = Nibble(code[pos--]) + 3;
            }

            if (state < weights.Length)
                weights[state] += weight;
            return;
        }

        var children = splitSides + 1;
        for (var i = 0; i < children; i++)
            Walk(code, ref pos, weight / children, weights, depth + 1);
    }

    private static int Nibble(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return 0;
    }
}
