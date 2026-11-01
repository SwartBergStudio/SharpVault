// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;

namespace SharpVault.Application.Features.Users.Access;

public static class UserProfileText
{
    public const int DisplayNameMaxLength = 256;

    public const int EmailMaxLength = 320;

    public const int IpAddressMaxLength = 45;

    public static string NormalizeDisplayName(string? value) => Clean(value, DisplayNameMaxLength);

    public static string? NormalizeEmail(string? value)
    {
        var cleaned = Clean(value, EmailMaxLength);
        return cleaned.Contains('@', StringComparison.Ordinal) ? cleaned : null;
    }

    public static string? NormalizeIpAddress(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > IpAddressMaxLength)
        {
            return null;
        }

        return IPAddress.TryParse(value, out var address) ? address.ToString() : null;
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var kept = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!IsRemovable(character))
            {
                kept.Append(character);
            }
        }

        var cleaned = kept.ToString().Trim();
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength];
        }

        if (cleaned.Length > 0 && char.IsHighSurrogate(cleaned[^1]))
        {
            cleaned = cleaned[..^1];
        }

        return cleaned.Trim();
    }

    private static bool IsRemovable(char character) =>
        char.IsControl(character) || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format;
}
