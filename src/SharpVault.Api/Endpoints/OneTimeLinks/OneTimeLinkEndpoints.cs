// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.OneTimeLinks.Queries;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Api.Endpoints.OneTimeLinks;

public static class OneTimeLinkEndpoints
{
    private const string LinkIdFormat = "D";

    public static IEndpointRouteBuilder MapOneTimeLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("OneTimeLinks");

        MapCreatorRoutes(group);
        MapOpenRoute(app);
        return app;
    }

    private static void MapCreatorRoutes(IEndpointRouteBuilder group)
    {
        group.MapGet(ApiRoutes.OneTimeLinkSettings, GetSettingsAsync)
            .WithName("OneTimeLinks_GetSettings")
            .WithSummary("Get the one-time link settings.")
            .WithDescription("Returns whether one-time links are enabled, the default lifetime and the maximum lifetime in minutes.")
            .Produces<OneTimeLinkSettingsDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapPost(ApiRoutes.OneTimeLinkSecretSnapshot, RevealSnapshotAsync)
            .RequireRateLimiting(RateLimitPolicies.Reveal)
            .WithName("OneTimeLinks_RevealSnapshot")
            .WithSummary("Read a secret so a link can be made from it.")
            .WithDescription("Returns the decrypted contents of a secret to its owner, or to a person with Manage permission, and writes an audit entry. Nothing is stored.")
            .Produces<OneTimeLinkSnapshotDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPost(ApiRoutes.OneTimeLinkSecret, CreateLinkAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("OneTimeLinks_Create")
            .WithSummary("Create a one-time link to a secret.")
            .WithDescription("Stores the encrypted snapshot the browser made. The link opens once, for anyone who has it, until it expires or is revoked.")
            .Produces<OneTimeLinkDto>(StatusCodes.Status201Created)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapGet(ApiRoutes.OneTimeLinks, ListLinksAsync)
            .WithName("OneTimeLinks_List")
            .WithSummary("List the one-time links the caller created.")
            .WithDescription("Returns a page of the caller's links that have not been opened, revoked or expired. Names come from the owner's vault and are shown only when they can be read.")
            .Produces<OneTimeLinkPageDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapDelete(ApiRoutes.OneTimeLink, RevokeLinkAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("OneTimeLinks_Revoke")
            .WithSummary("Revoke a one-time link.")
            .WithDescription("Deletes a link the caller created. The link can no longer be opened.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true);

        group.MapGet(ApiRoutes.OneTimeLinkNotices, ListNoticesAsync)
            .WithName("OneTimeLinks_ListNotices")
            .WithSummary("List notices that the caller's links were opened.")
            .WithDescription("Returns up to 20 notices, newest first. A notice never carries the recipient's address or agent.")
            .Produces<OneTimeLinkNoticeListDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapDelete(ApiRoutes.OneTimeLinkNotice, DismissNoticeAsync)
            .WithName("OneTimeLinks_DismissNotice")
            .WithSummary("Dismiss a one-time link notice.")
            .WithDescription("Removes one notice from the caller's list.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true);
    }

    private static void MapOpenRoute(IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.OneTimeLinkOpen, OpenLinkAsync)
            .RequireAuthorization(Policies.LinkGateway)
            .RequireRateLimiting(RateLimitPolicies.OneTimeLinkOpen)
            .WithTags("OneTimeLinks")
            .WithName("OneTimeLinks_Open")
            .WithSummary("Open a one-time link.")
            .WithDescription("Returns the payload once and deletes the link. Only the Web host can call it, with a signed proof of the visitor's address. Every state that cannot be opened returns 404.")
            .Produces<OneTimeLinkPayloadDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> GetSettingsAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetOneTimeLinkSettingsQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevealSnapshotAsync(Guid ownerId, Guid secretId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RevealOneTimeLinkSnapshotCommand(ownerId, secretId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateLinkAsync(
        Guid ownerId,
        Guid secretId,
        [FromBody] CreateOneTimeLinkRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new CreateOneTimeLinkCommand(ownerId, secretId, body.Payload, body.LifetimeMinutes);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToCreatedHttpResult(link => ApiRoutes.OneTimeLinks + "/" + link.Id.ToString(LinkIdFormat));
    }

    private static async Task<IResult> ListLinksAsync(
        [FromQuery(Name = ApiQuery.PageSize)] int? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListOneTimeLinksQuery(pageSize ?? OneTimeLinkLimits.DefaultPageSize, continuationToken);
        var result = await mediator.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeLinkAsync(Guid linkId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RevokeOneTimeLinkCommand(linkId), cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static async Task<IResult> ListNoticesAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListOneTimeLinkNoticesQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DismissNoticeAsync(string noticeId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DismissOneTimeLinkNoticeCommand(noticeId), cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static async Task<IResult> OpenLinkAsync(Guid linkId, HttpContext context, IMediator mediator, CancellationToken cancellationToken)
    {
        var command = new OpenOneTimeLinkCommand(
            linkId,
            context.User.FindFirst(LinkGatewayClaims.ClientIp)?.Value,
            context.User.FindFirst(LinkGatewayClaims.UserAgent)?.Value);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }
}
