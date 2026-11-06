// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Sharing.Queries;
using SharpVault.Application.Features.Transfers.Commands;
using SharpVault.Application.Features.Transfers.Queries;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Api.Endpoints.Transfers;

public static class TransferNoticeEndpoints
{
    private const int DefaultSharePageSize = 50;

    public static IEndpointRouteBuilder MapTransferNoticeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Transfers");

        group.MapGet(ApiRoutes.TransferNotices, ListNoticesAsync)
            .WithName("Transfers_ListNotices")
            .WithSummary("List transfer notices.")
            .WithDescription("Returns up to 20 notices about transfers that completed for the caller. Notices never carry a reason or category.")
            .Produces<TransferNoticeListDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapDelete(ApiRoutes.TransferNotice, DismissNoticeAsync)
            .WithName("Transfers_DismissNotice")
            .WithSummary("Dismiss a transfer notice.")
            .WithDescription("Removes one notice from the caller's list.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true);

        group.MapGet(ApiRoutes.TransferOwnedShares, ListOwnedSharesAsync)
            .WithName("Transfers_ListOwnedShares")
            .WithSummary("List the shares the caller gave.")
            .WithDescription("Returns a page of the caller's own share grants with item names decrypted under the caller's key. Other users' grants are never returned.")
            .Produces<OwnedSharePageDto>()
            .ProducesVaultProblems(itemRoute: false);

        return app;
    }

    private static async Task<IResult> ListNoticesAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListTransferNoticesQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DismissNoticeAsync(Guid transferId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DismissTransferNoticeCommand(transferId), cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static async Task<IResult> ListOwnedSharesAsync(
        [FromQuery(Name = ApiQuery.PageSize)] int? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListOwnedSharesQuery(pageSize ?? DefaultSharePageSize, continuationToken), cancellationToken);
        return result.ToHttpResult();
    }
}
