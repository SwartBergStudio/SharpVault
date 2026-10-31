// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

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

        var kept = new System.Text.StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value.Trim())
        {
            if (!char.IsControl(character))
            {
                kept.Append(character);
            }
        }

        if (kept.Length > maxLength)
        {
            kept.Length = maxLength;
        }

        if (kept.Length > 0 && char.IsHighSurrogate(kept[^1]))
        {
            kept.Length--;
        }

        return kept.ToString();
    }
}
