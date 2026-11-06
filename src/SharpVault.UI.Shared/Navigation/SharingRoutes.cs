// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Navigation;

public static class SharingRoutes
{
    public const string Shared = "/shared";

    public static string Secret(Guid ownerId, Guid secretId) => $"{Shared}/{ownerId:D}/secrets/{secretId:D}";

    public static string EditSecret(Guid ownerId, Guid secretId) => $"{Secret(ownerId, secretId)}/edit";

    public static string Folder(Guid ownerId, Guid folderId) => $"{Shared}/{ownerId:D}/folders/{folderId:D}";
}
