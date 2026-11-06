// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Features.Users.Queries;

public sealed record GetUserCountsQuery(Guid UserId) : IRequest<Result<UserCountsDto>>
{
    public override string ToString() => nameof(GetUserCountsQuery);
}

public sealed class GetUserCountsQueryValidator : AbstractValidator<GetUserCountsQuery>
{
    public GetUserCountsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}

public sealed class GetUserCountsQueryHandler(
    AdminActorGuard guard,
    IUserStore users,
    IVaultStore vaults) : IRequestHandler<GetUserCountsQuery, Result<UserCountsDto>>
{
    public async Task<Result<UserCountsDto>> Handle(GetUserCountsQuery request, CancellationToken cancellationToken)
    {
        if (await guard.RequireAsync(cancellationToken) is null)
        {
            return Result<UserCountsDto>.Forbidden();
        }

        if (await users.GetAsync(request.UserId, cancellationToken) is null)
        {
            return Result<UserCountsDto>.NotFound();
        }

        var inventory = await TransferInventoryReader.ReadAsync(vaults, request.UserId, TransferLimits.InventoryMaxPages, cancellationToken);
        var shares = await vaults.CountShareGrantsByOwnerAsync(request.UserId, TransferLimits.InventoryMaxPages, cancellationToken);
        var secretCount = inventory.Rows.Count(row => row.Kind == VaultItemKind.ActiveSecret);

        return Result<UserCountsDto>.Success(new UserCountsDto
        {
            ItemCount = secretCount,
            ShareCount = shares.Count,
            CountsAreCapped = inventory.IsCapped || shares.IsCapped,
        });
    }
}
