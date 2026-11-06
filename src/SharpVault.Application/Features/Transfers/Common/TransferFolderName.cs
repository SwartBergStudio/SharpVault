// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferFolderName
{
    private const string Prefix = "Transferred from ";
    private const string DateFormat = "yyyy-MM-dd";
    private const string FallbackName = "a former user";

    public static string Build(string? sourceDisplayName, DateTimeOffset decidedAt)
    {
        var suffix = $" ({decidedAt.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)})";
        var room = VaultLimits.FolderNameMaxLength - Prefix.Length - suffix.Length;
        var name = string.IsNullOrWhiteSpace(sourceDisplayName) ? FallbackName : sourceDisplayName.Trim();

        return Prefix + Truncate(name, room) + suffix;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }
}
