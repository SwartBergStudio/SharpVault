// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;

namespace SharpVault.UI.Shared.Components.Sharing;

public static class SharedItemText
{
    public const string NotFiled = "Not filed";
    public const string FiledUnknown = "Filed";
    public const string FolderKind = "Folder";
    public const string UnreadableName = "Unreadable item";

    public static string Name(SharedItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.IsDamaged || string.IsNullOrWhiteSpace(item.Name) ? UnreadableName : item.Name;
    }

    public static string KindLabel(SharedItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Kind == SharedItemKindDto.Folder)
        {
            return FolderKind;
        }

        return item.Type is { } type ? SecretTypeLabels.Label(type) : string.Empty;
    }

    public static string Href(SharedItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind == SharedItemKindDto.Folder
            ? SharingRoutes.Folder(item.OwnerId, item.ItemId)
            : SharingRoutes.Secret(item.OwnerId, item.ItemId);
    }

    public static Guid? EffectiveFolderId(SharedItemDto item, IReadOnlyList<FolderDto>? ownFolders)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.FiledFolderId is not { } filed)
        {
            return null;
        }

        return ownFolders is null || ownFolders.Any(folder => folder.Id == filed) ? filed : null;
    }

    public static string FiledLabel(SharedItemDto item, IReadOnlyList<FolderDto>? ownFolders)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (EffectiveFolderId(item, ownFolders) is not { } filed)
        {
            return NotFiled;
        }

        var folder = ownFolders?.FirstOrDefault(candidate => candidate.Id == filed);
        return folder is null ? FiledUnknown : FolderNodes.NameOf(folder);
    }

    public static string ElementKey(SharedItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var kind = item.Kind == SharedItemKindDto.Folder ? "folder" : "secret";
        return $"{kind}-{item.ItemId:D}";
    }
}
