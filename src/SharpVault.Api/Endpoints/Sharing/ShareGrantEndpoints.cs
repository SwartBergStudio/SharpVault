// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Application.Features.Sharing.Grants.Queries;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Api.Endpoints.Sharing;

public static class ShareGrantEndpoints
{
    public static IEndpointRouteBuilder MapShareGrantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Sharing");

        MapSecretGrants(group);
        MapFolderGrants(group);
        return app;
    }

    private static void MapSecretGrants(IEndpointRouteBuilder group)
    {
        group.MapGet(ApiRoutes.SecretGrants, ListSecretGrantsAsync)
            .WithName("Sharing_ListSecretGrants")
            .WithSummary("List who a secret is shared with.")
            .WithDescription("Returns the grants on a secret. Only the owner, or a person with Manage permission, can see them.")
            .Produces<ShareGrantListDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPost(ApiRoutes.SecretGrants, CreateSecretGrantAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("Sharing_CreateSecretGrant")
            .WithSummary("Share a secret.")
            .WithDescription("Shares a secret with a member of the tenant at the View, Edit or Manage permission.")
            .Produces<ShareGrantDto>(StatusCodes.Status201Created)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPut(ApiRoutes.SecretGrant, ChangeSecretGrantAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("Sharing_ChangeSecretGrant")
            .WithSummary("Change the permission on a secret share.")
            .WithDescription("Changes a grant on a secret. The If-Match header carries the concurrency token from the last response.")
            .Produces<ShareGrantDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapDelete(ApiRoutes.SecretGrant, RevokeSecretGrantAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("Sharing_RevokeSecretGrant")
            .WithSummary("Stop sharing a secret with a person.")
            .WithDescription("Removes a grant on a secret. The person loses access to it at once.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);
    }

    private static void MapFolderGrants(IEndpointRouteBuilder group)
    {
        group.MapGet(ApiRoutes.FolderGrants, ListFolderGrantsAsync)
            .WithName("Sharing_ListFolderGrants")
            .WithSummary("List who a folder is shared with.")
            .WithDescription("Returns the grants on a folder. Only the owner, or a person with Manage permission, can see them.")
            .Produces<ShareGrantListDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPost(ApiRoutes.FolderGrants, CreateFolderGrantAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("Sharing_CreateFolderGrant")
            .WithSummary("Share a folder.")
            .WithDescription("Shares a folder and everything inside it with a member of the tenant at the View, Edit or Manage permission.")
            .Produces<ShareGrantDto>(StatusCodes.Status201Created)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPut(ApiRoutes.FolderGrant, ChangeFolderGrantAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("Sharing_ChangeFolderGrant")
            .WithSummary("Change the permission on a folder share.")
            .WithDescription("Changes a grant on a folder. The If-Match header carries the concurrency token from the last response.")
            .Produces<ShareGrantDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapDelete(ApiRoutes.FolderGrant, RevokeFolderGrantAsync)
            .RequireRateLimiting(RateLimitPolicies.Sharing)
            .WithName("Sharing_RevokeFolderGrant")
            .WithSummary("Stop sharing a folder with a person.")
            .WithDescription("Removes a grant on a folder. The person loses access to it at once.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);
    }

    private static async Task<IResult> ListSecretGrantsAsync(Guid ownerId, Guid secretId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListShareGrantsQuery(ownerId, ShareItemKind.Secret, secretId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateSecretGrantAsync(
        Guid ownerId,
        Guid secretId,
        [FromBody] CreateShareGrantRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new CreateShareGrantCommand(ownerId, ShareItemKind.Secret, secretId, body.RecipientId, body.Permission);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToCreatedHttpResult(grant => GrantLocation(ApiRoutes.SecretGrants, ownerId, secretId, grant.RecipientId));
    }

    private static async Task<IResult> ChangeSecretGrantAsync(
        Guid ownerId,
        Guid secretId,
        Guid recipientId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        [FromBody] ChangeShareGrantRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new ChangeShareGrantCommand(ownerId, ShareItemKind.Secret, secretId, recipientId, ifMatch ?? string.Empty, body.Permission);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeSecretGrantAsync(
        Guid ownerId,
        Guid secretId,
        Guid recipientId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new RevokeShareGrantCommand(ownerId, ShareItemKind.Secret, secretId, recipientId);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static async Task<IResult> ListFolderGrantsAsync(Guid ownerId, Guid folderId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListShareGrantsQuery(ownerId, ShareItemKind.Folder, folderId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateFolderGrantAsync(
        Guid ownerId,
        Guid folderId,
        [FromBody] CreateShareGrantRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new CreateShareGrantCommand(ownerId, ShareItemKind.Folder, folderId, body.RecipientId, body.Permission);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToCreatedHttpResult(grant => GrantLocation(ApiRoutes.FolderGrants, ownerId, folderId, grant.RecipientId));
    }

    private static async Task<IResult> ChangeFolderGrantAsync(
        Guid ownerId,
        Guid folderId,
        Guid recipientId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        [FromBody] ChangeShareGrantRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new ChangeShareGrantCommand(ownerId, ShareItemKind.Folder, folderId, recipientId, ifMatch ?? string.Empty, body.Permission);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeFolderGrantAsync(
        Guid ownerId,
        Guid folderId,
        Guid recipientId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new RevokeShareGrantCommand(ownerId, ShareItemKind.Folder, folderId, recipientId);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static string GrantLocation(string collectionRoute, Guid ownerId, Guid itemId, Guid recipientId)
    {
        var path = collectionRoute
            .Replace("{ownerId:guid}", ownerId.ToString("D"), StringComparison.Ordinal)
            .Replace("{secretId:guid}", itemId.ToString("D"), StringComparison.Ordinal)
            .Replace("{folderId:guid}", itemId.ToString("D"), StringComparison.Ordinal);
        return $"{path}/{recipientId:D}";
    }
}
