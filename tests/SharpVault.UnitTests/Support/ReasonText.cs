// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support;

public static class ReasonText
{
    private const string HighToken = "{HIGH}";
    private const string LowToken = "{LOW}";
    private const char HighSurrogate = '\uD83D';
    private const char LowSurrogate = '\uDE00';

    public static string Expand(string template) => template
        .Replace(HighToken, HighSurrogate.ToString(), StringComparison.Ordinal)
        .Replace(LowToken, LowSurrogate.ToString(), StringComparison.Ordinal);
}
