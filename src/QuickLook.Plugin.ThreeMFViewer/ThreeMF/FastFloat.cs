using System.Globalization;

namespace QuickLook.Plugin.ThreeMFViewer.ThreeMF;

/// <summary>
/// Number parsing for the millions of vertex coordinates of a large 3MF, several times
/// faster than <see cref="double.Parse(string)"/>. Unusual input falls back to it.
/// </summary>
internal static class FastFloat
{
    private static readonly double[] PowersOf10 =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10,
        1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    public static double Parse(string s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;

        var i = 0;
        var negative = false;
        if (s[0] == '-' || s[0] == '+')
        {
            negative = s[0] == '-';
            i++;
        }

        ulong mantissa = 0;
        var digits = 0;
        var exponent = 0;
        var any = false;

        for (; i < s.Length && IsDigit(s[i]); i++, any = true)
        {
            if (digits < 18)
            {
                mantissa = mantissa * 10 + (ulong)(s[i] - '0');
                if (mantissa != 0) digits++;
            }
            else
            {
                exponent++;
            }
        }

        if (i < s.Length && s[i] == '.')
        {
            for (i++; i < s.Length && IsDigit(s[i]); i++, any = true)
            {
                if (digits < 18)
                {
                    mantissa = mantissa * 10 + (ulong)(s[i] - '0');
                    if (mantissa != 0) digits++;
                    exponent--;
                }
            }
        }

        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            var expNegative = false;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            {
                expNegative = s[i] == '-';
                i++;
            }

            var exp = 0;
            var expAny = false;
            for (; i < s.Length && IsDigit(s[i]); i++, expAny = true)
            {
                if (exp < 10000)
                    exp = exp * 10 + (s[i] - '0');
            }

            if (!expAny)
                return Fallback(s);
            exponent += expNegative ? -exp : exp;
        }

        if (!any || i != s.Length || exponent < -22 || exponent > 22)
            return Fallback(s);

        double value = mantissa;
        value = exponent < 0 ? value / PowersOf10[-exponent] : value * PowersOf10[exponent];
        return negative ? -value : value;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static double Fallback(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
