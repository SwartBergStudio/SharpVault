// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public sealed class ShareAccessResolver(IVaultStore store, ICurrentUser currentUser)
{
    public async Task<SecretPermission> ForSecretAsync(Guid ownerId, Guid secretId, SecretPermission stopAt, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId;
        var directTask = store.GetShareGrantAsync(ownerId, ShareItemKind.Secret, secretId, callerId, cancellationToken);
        var folderGrantsTask = store.ListFolderGrantsToRecipientAsync(ownerId, callerId, cancellationToken);
        await Task.WhenAll(directTask, folderGrantsTask);

        var direct = await directTask;
        var folderGrants = await folderGrantsTask;
        var best = direct is null ? SecretPermission.None : SharingEnumMap.ToAccess(direct.Value.Permission);
        if (folderGrants.Count == 0 || best >= stopAt)
        {
            return best;
        }

        var secret = await store.GetSecretAsync(ownerId, secretId, cancellationToken);
        if (secret is null)
        {
            return SecretPermission.None;
        }

        return await WalkAsync(ownerId, secret.Value.FolderId, GrantsByFolder(folderGrants), best, stopAt, cancellationToken);
    }

    public async Task<SecretPermission> ForFolderAsync(Guid ownerId, Guid folderId, SecretPermission stopAt, CancellationToken cancellationToken)
    {
        var folderGrants = await store.ListFolderGrantsToRecipientAsync(ownerId, currentUser.UserId, cancellationToken);
        if (folderGrants.Count == 0)
        {
            return SecretPermission.None;
        }

        return await WalkAsync(ownerId, folderId, GrantsByFolder(folderGrants), SecretPermission.None, stopAt, cancellationToken);
    }

    private static Dictionary<Guid, SecretPermission> GrantsByFolder(IReadOnlyList<ShareGrant> grants)
    {
        var byFolder = new Dictionary<Guid, SecretPermission>(grants.Count);
        foreach (var grant in grants)
        {
            var permission = SharingEnumMap.ToAccess(grant.Permission);
            if (!byFolder.TryGetValue(grant.ItemId, out var existing) || permission > existing)
            {
                byFolder[grant.ItemId] = permission;
            }
        }

        return byFolder;
    }

    private async Task<SecretPermission> WalkAsync(
        Guid ownerId,
        Guid? startFolderId,
        Dictionary<Guid, SecretPermission> grantsByFolder,
        SecretPermission best,
        SecretPermission stopAt,
        CancellationToken cancellationToken)
    {
        var current = startFolderId;
        for (var depth = 0; depth < VaultLimits.MaxFolderDepth && current is { } folderId; depth++)
        {
            var folder = await store.GetFolderAsync(ownerId, folderId, cancellationToken);
            if (folder is null)
            {
                break;
            }

            if (grantsByFolder.TryGetValue(folderId, out var granted) && granted > best)
            {
                best = granted;
            }

            if (best >= stopAt)
            {
                break;
            }

            current = folder.Value.ParentId;
        }

        return best;
    }
}
