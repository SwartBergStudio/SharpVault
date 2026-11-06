// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;

namespace SharpVault.Application.Features.Vault.Common;

public sealed class SecretAccessPolicy(ICurrentUser currentUser, ShareAccessResolver resolver) : ISecretAccessPolicy
{
    public ValueTask<SecretAccessGrant?> ResolveAsync(Guid secretId, SecretPermission required, CancellationToken cancellationToken) =>
        ValueTask.FromResult<SecretAccessGrant?>(new SecretAccessGrant(currentUser.UserId, SecretPermission.Owner));

    public async ValueTask<SecretAccessGrant?> ResolveSharedAsync(Guid ownerId, Guid secretId, SecretPermission required, CancellationToken cancellationToken)
    {
        if (ownerId == currentUser.UserId)
        {
            return null;
        }

        var permission = await resolver.ForSecretAsync(ownerId, secretId, required, cancellationToken);
        return permission < SecretPermission.View ? null : new SecretAccessGrant(ownerId, permission);
    }
}
