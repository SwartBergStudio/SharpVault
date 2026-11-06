// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Navigation;

public static class VaultRoutes
{
    public const string Vault = "/vault";
    public const string Unfiled = "/vault/unfiled";
    public const string NewSecret = "/vault/secrets/new";
    public const string RecycleBin = "/vault/recycle-bin";
    public const string SearchParameter = "q";
    public const string FolderIdParameter = "folderId";

    public static string Folder(Guid folderId) => $"{Vault}/folders/{folderId:D}";

    public static string Secret(Guid secretId) => $"{Vault}/secrets/{secretId:D}";

    public static string EditSecret(Guid secretId) => $"{Secret(secretId)}/edit";

    public static string History(Guid secretId) => $"{Secret(secretId)}/history";

    public static string WithSearch(string route, string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        if (string.IsNullOrWhiteSpace(term))
        {
            return route;
        }

        var separator = route.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{route}{separator}{SearchParameter}={Uri.EscapeDataString(term)}";
    }
}
