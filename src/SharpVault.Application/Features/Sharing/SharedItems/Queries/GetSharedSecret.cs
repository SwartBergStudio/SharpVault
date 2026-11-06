// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.SharedItems.Queries;

public sealed record GetSharedSecretQuery(Guid OwnerId, Guid SecretId) : IRequest<Result<SharedSecretDto>>;

public sealed class GetSharedSecretQueryValidator : AbstractValidator<GetSharedSecretQuery>
{
    public GetSharedSecretQueryValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
    }
}

public sealed class GetSharedSecretQueryHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    IUserStore users,
    ISecretAccessPolicy accessPolicy) : IRequestHandler<GetSharedSecretQuery, Result<SharedSecretDto>>
{
    public async Task<Result<SharedSecretDto>> Handle(GetSharedSecretQuery request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.ResolveSharedAsync(request.OwnerId, request.SecretId, SecretPermission.Manage, cancellationToken);
        if (grant is null || grant.Granted < SecretPermission.View)
        {
            return Result<SharedSecretDto>.NotFound();
        }

        var secretTask = store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        var readTask = keyRings.OpenForReadAsync(grant.OwnerId, cancellationToken);
        var ownerTask = users.GetAsync(grant.OwnerId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(readTask, secretTask, ownerTask);

        using var read = await readTask;
        var secret = await secretTask;
        if (secret is null)
        {
            return Result<SharedSecretDto>.NotFound();
        }

        var owner = await ownerTask;
        return Result<SharedSecretDto>.Success(new SharedSecretDto
        {
            Item = SecretDtoFactory.ForRecipient(secret, read.Vault),
            OwnerId = grant.OwnerId,
            OwnerDisplayName = owner?.Value.DisplayName ?? SharingMessages.UnknownOwner,
            Permission = SharedPermissionMap.ToDto(grant.Granted),
        });
    }
}
