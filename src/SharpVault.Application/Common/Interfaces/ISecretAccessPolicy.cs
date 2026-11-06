// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Common.Interfaces;

public interface ISecretAccessPolicy
{
    ValueTask<SecretAccessGrant?> ResolveAsync(Guid secretId, SecretPermission required, CancellationToken cancellationToken);

    ValueTask<SecretAccessGrant?> ResolveSharedAsync(Guid ownerId, Guid secretId, SecretPermission required, CancellationToken cancellationToken);
}
