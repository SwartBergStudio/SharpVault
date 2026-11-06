// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Components.Vault;

public static class RecycleBinItemText
{
    public const string DamagedName = "Unreadable item";
    public const string UnnamedName = "Unnamed item";

    public static string KindSlug(RecycleBinItemKindDto kind) => kind switch
    {
        RecycleBinItemKindDto.Secret => "secret",
        RecycleBinItemKindDto.Folder => "folder",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string KindLabel(RecycleBinItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind switch
        {
            RecycleBinItemKindDto.Folder => "Folder",
            _ => item.Type is { } type ? SecretTypeLabels.Label(type) : "Secret",
        };
    }

    public static string DisplayName(RecycleBinItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            return item.Name;
        }

        return item.IsDamaged ? DamagedName : UnnamedName;
    }

    public static string ExpiresText(RecycleBinItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return $"Expires {StampText.For(item.PurgeAfter)}";
    }

    public static string IdFor(string prefix, RecycleBinItemDto item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(item);

        return $"{prefix}-{KindSlug(item.Kind)}-{item.Id:D}";
    }
}
