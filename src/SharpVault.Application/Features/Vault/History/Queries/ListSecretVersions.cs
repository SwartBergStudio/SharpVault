// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mapster;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.History.Queries;

public sealed record ListSecretVersionsQuery(Guid SecretId) : IRequest<Result<SecretVersionListDto>>;

public sealed class ListSecretVersionsQueryValidator : AbstractValidator<ListSecretVersionsQuery>
{
    public ListSecretVersionsQueryValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
    }
}

public sealed class ListSecretVersionsQueryHandler(IVaultStore store, ISecretAccessPolicy accessPolicy)
    : IRequestHandler<ListSecretVersionsQuery, Result<SecretVersionListDto>>
{
    public async Task<Result<SecretVersionListDto>> Handle(ListSecretVersionsQuery request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SecretId, SecretPermission.View, cancellationToken);
        if (grant is null)
        {
            return Result<SecretVersionListDto>.NotFound();
        }

        var secret = await store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        if (secret is null)
        {
            return Result<SecretVersionListDto>.NotFound();
        }

        var versions = await store.ListVersionsAsync(grant.OwnerId, request.SecretId, cancellationToken);
        var ordered = SecretVersionHistory.NewestFirst(versions);

        return Result<SecretVersionListDto>.Success(new SecretVersionListDto
        {
            Items = [.. ordered.Select(version => version.Value.Adapt<SecretVersionDto>())],
        });
    }
}
