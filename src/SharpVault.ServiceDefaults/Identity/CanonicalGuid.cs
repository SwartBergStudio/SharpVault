// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.ServiceDefaults.Identity;

public static class CanonicalGuid
{
    private const string GuidFormat = "D";
    private const int CanonicalLength = 36;

    public static bool TryParse(string? value, out Guid result)
    {
        result = Guid.Empty;
        return value is { Length: CanonicalLength } && Guid.TryParseExact(value, GuidFormat, out result);
    }
}
