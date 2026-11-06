// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public sealed class ShareManagementGate(IVaultStore store, ShareAccessResolver resolver, ICurrentUser currentUser)
{
    public async Task<SecretPermission?> RequireManageAsync(
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        bool allowRecycled,
        CancellationToken cancellationToken)
    {
        if (ownerId == currentUser.UserId)
        {
            return await OwnsAsync(ownerId, kind, itemId, allowRecycled, cancellationToken) ? SecretPermission.Owner : null;
        }

        var permission = kind switch
        {
            ShareItemKind.Secret => await resolver.ForSecretAsync(ownerId, itemId, SecretPermission.Manage, cancellationToken),
            ShareItemKind.Folder => await resolver.ForFolderAsync(ownerId, itemId, SecretPermission.Manage, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        if (permission < SecretPermission.Manage)
        {
            return null;
        }

        if (kind == ShareItemKind.Secret && await store.GetSecretAsync(ownerId, itemId, cancellationToken) is null)
        {
            return null;
        }

        return SecretPermission.Manage;
    }

    private async Task<bool> OwnsAsync(Guid ownerId, ShareItemKind kind, Guid itemId, bool allowRecycled, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ShareItemKind.Secret:
                if (await store.GetSecretAsync(ownerId, itemId, cancellationToken) is not null)
                {
                    return true;
                }

                return allowRecycled && await store.GetRecycledSecretAsync(ownerId, itemId, cancellationToken) is not null;
            case ShareItemKind.Folder:
                if (await store.GetFolderAsync(ownerId, itemId, cancellationToken) is not null)
                {
                    return true;
                }

                return allowRecycled && await store.GetRecycledFolderAsync(ownerId, itemId, cancellationToken) is not null;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
