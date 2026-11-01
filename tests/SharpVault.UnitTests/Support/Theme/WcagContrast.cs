// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;

namespace SharpVault.UnitTests.Support.Theme;

internal static class WcagContrast
{
    private const double LinearThreshold = 0.04045;
    private const double LowDivisor = 12.92;
    private const double Offset = 0.055;
    private const double Divisor = 1.055;
    private const double Exponent = 2.4;
    private const double RedWeight = 0.2126;
    private const double GreenWeight = 0.7152;
    private const double BlueWeight = 0.0722;
    private const double LuminanceOffset = 0.05;

    public static double Ratio(string hexA, string hexB)
    {
        var first = Luminance(hexA);
        var second = Luminance(hexB);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + LuminanceOffset) / (darker + LuminanceOffset);
    }

    public static double Luminance(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"Expected #RRGGBB but found '{hex}'.");
        }

        var red = Channel(hex.Substring(1, 2));
        var green = Channel(hex.Substring(3, 2));
        var blue = Channel(hex.Substring(5, 2));
        return (RedWeight * red) + (GreenWeight * green) + (BlueWeight * blue);
    }

    private static double Channel(string twoHexDigits)
    {
        var value = int.Parse(twoHexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return value <= LinearThreshold
            ? value / LowDivisor
            : Math.Pow((value + Offset) / Divisor, Exponent);
    }
}
