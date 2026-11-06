// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Transfers.Commands;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Queries;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Api.Endpoints.Transfers;

public static class AdminTransferEndpoints
{
    private const int DefaultPageSize = 25;

    public static IEndpointRouteBuilder MapAdminTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.Admin)
            .RequireRateLimiting(RateLimitPolicies.AdminActions)
            .WithTags("Admin transfers");

        group.MapGet(ApiRoutes.AdminTransferPreview, PreviewTransferAsync)
            .WithName("AdminTransfers_Preview")
            .WithSummary("Preview a transfer.")
            .WithDescription("Returns counts, opaque folder references and the state of the source user before a transfer. Opens no key and returns no names.")
            .Produces<TransferPreviewDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapPost(ApiRoutes.AdminTransfers, CreateTransferAsync)
            .WithName("AdminTransfers_Create")
            .WithSummary("Request a transfer.")
            .WithDescription("Creates a pending transfer request. A second administrator must approve it before any data moves.")
            .Produces<TransferDto>(StatusCodes.Status201Created)
            .ProducesVaultProblems(itemRoute: false);

        group.MapGet(ApiRoutes.AdminTransfers, ListTransfersAsync)
            .WithName("AdminTransfers_List")
            .WithSummary("List transfers.")
            .WithDescription("Returns a page of transfers the caller is not a party to.")
            .Produces<TransferPageDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapGet(ApiRoutes.AdminTransfer, GetTransferAsync)
            .WithName("AdminTransfers_Get")
            .WithSummary("Get a transfer.")
            .WithDescription("Returns one transfer. Transfers the caller is a party to are not found.")
            .Produces<TransferDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPost(ApiRoutes.AdminTransferApprove, ApproveTransferAsync)
            .WithName("AdminTransfers_Approve")
            .WithSummary("Approve a transfer.")
            .WithDescription("Approves a pending transfer. The requester cannot approve. The data job starts after approval.")
            .Produces<TransferDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.AdminTransferReject, RejectTransferAsync)
            .WithName("AdminTransfers_Reject")
            .WithSummary("Reject a transfer.")
            .WithDescription("Rejects a pending transfer with a reason. The requester can withdraw their own request this way.")
            .Produces<TransferDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.AdminTransferAbort, AbortTransferAsync)
            .WithName("AdminTransfers_Abort")
            .WithSummary("Abort a transfer.")
            .WithDescription("Aborts a transfer before its point of no return and removes staged copies. Not possible once the source is released.")
            .Produces<TransferDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        return app;
    }

    private static async Task<IResult> PreviewTransferAsync(
        [FromQuery(Name = ApiQuery.SourceUserId)] Guid sourceUserId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransferPreviewQuery(sourceUserId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateTransferAsync(
        [FromBody] CreateTransferRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new CreateTransferCommand(
            body.SourceUserId,
            body.TargetUserId,
            TransferEnumMap.ToDomain(body.Scope),
            body.FolderIds,
            TransferEnumMap.ToDomain(body.Category),
            body.Reason);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToCreatedHttpResult(transfer => $"{ApiRoutes.AdminTransfers}/{transfer.Id:D}");
    }

    private static async Task<IResult> ListTransfersAsync(
        [FromQuery(Name = ApiQuery.PageSize)] int? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListTransfersQuery(pageSize ?? DefaultPageSize, continuationToken), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetTransferAsync(Guid transferId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransferQuery(transferId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ApproveTransferAsync(
        Guid transferId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ApproveTransferCommand(transferId, ifMatch ?? string.Empty), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RejectTransferAsync(
        Guid transferId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        [FromBody] TransferDecisionRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RejectTransferCommand(transferId, ifMatch ?? string.Empty, body.Reason), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AbortTransferAsync(
        Guid transferId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        [FromBody] TransferDecisionRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new AbortTransferCommand(transferId, ifMatch ?? string.Empty, body.Reason), cancellationToken);
        return result.ToHttpResult();
    }
}
