// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Secrets.Queries;

public sealed record GetSecretQuery(Guid SecretId) : IRequest<Result<SecretDto>>;

public sealed class GetSecretQueryValidator : AbstractValidator<GetSecretQuery>
{
    public GetSecretQueryValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
    }
}

public sealed class GetSecretQueryHandler(IVaultStore store, VaultKeyRingService keyRings, ISecretAccessPolicy accessPolicy)
    : IRequestHandler<GetSecretQuery, Result<SecretDto>>
{
    public async Task<Result<SecretDto>> Handle(GetSecretQuery request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SecretId, SecretPermission.View, cancellationToken);
        if (grant is null)
        {
            return Result<SecretDto>.NotFound();
        }

        var secretTask = store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        var readTask = keyRings.OpenForReadAsync(grant.OwnerId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(readTask, secretTask);

        using var read = await readTask;
        var secret = await secretTask;
        return secret is null
            ? Result<SecretDto>.NotFound()
            : Result<SecretDto>.Success(SecretDtoFactory.Create(secret, read.Vault));
    }
}
