// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Models;

public sealed record SecretFolderFilter(SecretFolderScope Scope, Guid? FolderId)
{
    public static SecretFolderFilter All { get; } = new(SecretFolderScope.All, null);

    public static SecretFolderFilter Unfiled { get; } = new(SecretFolderScope.Unfiled, null);

    public static SecretFolderFilter InFolder(Guid id) => new(SecretFolderScope.Folder, id);
}
