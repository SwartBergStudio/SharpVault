// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class StubSecretAccessPolicy : ISecretAccessPolicy
{
    public SecretAccessGrant? Grant { get; set; }

    public List<(Guid SecretId, SecretPermission Required)> Requests { get; } = [];

    public SecretAccessGrant? SharedGrant { get; set; }

    public List<(Guid OwnerId, Guid SecretId, SecretPermission Required)> SharedRequests { get; } = [];

    public ValueTask<SecretAccessGrant?> ResolveAsync(Guid secretId, SecretPermission required, CancellationToken cancellationToken)
    {
        Requests.Add((secretId, required));
        return ValueTask.FromResult(Grant);
    }

    public ValueTask<SecretAccessGrant?> ResolveSharedAsync(Guid ownerId, Guid secretId, SecretPermission required, CancellationToken cancellationToken)
    {
        SharedRequests.Add((ownerId, secretId, required));
        return ValueTask.FromResult(SharedGrant);
    }
}
