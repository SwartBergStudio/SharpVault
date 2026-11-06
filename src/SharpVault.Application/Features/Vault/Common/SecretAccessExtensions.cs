// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Features.Vault.Common;

public static class SecretAccessExtensions
{
    public static async Task<SecretAccessGrant?> RequireAsync(
        this ISecretAccessPolicy policy,
        Guid secretId,
        SecretPermission required,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var grant = await policy.ResolveAsync(secretId, required, cancellationToken);
        return grant is not null && grant.Granted >= required ? grant : null;
    }

    public static async Task<SecretAccessGrant?> RequireAsync(
        this ISecretAccessPolicy policy,
        Guid? sharedOwnerId,
        Guid secretId,
        SecretPermission required,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var grant = sharedOwnerId is { } ownerId
            ? await policy.ResolveSharedAsync(ownerId, secretId, required, cancellationToken)
            : await policy.ResolveAsync(secretId, required, cancellationToken);
        return grant is not null && grant.Granted >= required ? grant : null;
    }
}
