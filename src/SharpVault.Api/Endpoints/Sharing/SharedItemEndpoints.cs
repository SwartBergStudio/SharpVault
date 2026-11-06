// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Sharing.SharedItems.Commands;
using SharpVault.Application.Features.Sharing.SharedItems.Queries;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;

namespace SharpVault.Api.Endpoints.Sharing;

public static class SharedItemEndpoints
{
    public static IEndpointRouteBuilder MapSharedItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Sharing");

        group.MapGet(ApiRoutes.SharedItems, ListSharedItemsAsync)
            .WithName("Sharing_ListSharedItems")
            .WithSummary("List items shared with the caller.")
            .WithDescription("Returns a page of the secrets and folders that were shared directly with the caller. No value or notes are returned.")
            .Produces<SharedItemPageDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapGet(ApiRoutes.SharedSecret, GetSharedSecretAsync)
            .WithName("Sharing_GetSharedSecret")
            .WithSummary("Get a shared secret.")
            .WithDescription("Returns the metadata of a secret shared with the caller. No value or notes are returned.")
            .Produces<SharedSecretDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPut(ApiRoutes.SharedSecret, UpdateSharedSecretAsync)
            .WithName("Sharing_UpdateSharedSecret")
            .WithSummary("Update a shared secret.")
            .WithDescription("Updates a secret shared with Edit or Manage permission. The If-Match header carries the concurrency token from the last response.")
            .Produces<SecretDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.SharedSecretReveal, RevealSharedSecretAsync)
            .RequireRateLimiting(RateLimitPolicies.Reveal)
            .WithName("Sharing_RevealSharedSecret")
            .WithSummary("Reveal a shared secret value or notes.")
            .WithDescription("Decrypts the value or the notes of a shared secret and writes an audit entry before returning it.")
            .Produces<RevealedValueDto>()
            .ProducesVaultProblems(itemRoute: true)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet(ApiRoutes.SharedFolder, GetSharedFolderAsync)
            .WithName("Sharing_GetSharedFolder")
            .WithSummary("Get a shared folder.")
            .WithDescription("Returns a page of the contents of a folder shared with the caller, or of a folder inside one. No value or notes are returned.")
            .Produces<SharedFolderContentsDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPut(ApiRoutes.SharedSecretFiling, FileSharedSecretAsync)
            .WithName("Sharing_FileSharedSecret")
            .WithSummary("File a shared secret.")
            .WithDescription("Files a directly shared secret in one of the caller's own folders, or removes it from a folder. The owner's vault does not change.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true);

        group.MapPut(ApiRoutes.SharedFolderFiling, FileSharedFolderAsync)
            .WithName("Sharing_FileSharedFolder")
            .WithSummary("File a shared folder.")
            .WithDescription("Files a directly shared folder in one of the caller's own folders, or removes it from a folder. The owner's vault does not change.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true);

        return app;
    }

    private static async Task<IResult> ListSharedItemsAsync(
        [FromQuery(Name = ApiQuery.FiledFolderId)] Guid? filedFolderId,
        [FromQuery(Name = ApiQuery.PageSize)] int? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListSharedItemsQuery(filedFolderId, pageSize ?? ListSharedItemsQuery.DefaultPageSize, continuationToken);
        var result = await mediator.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSharedSecretAsync(
        Guid ownerId,
        Guid secretId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetSharedSecretQuery(ownerId, secretId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateSharedSecretAsync(
        Guid ownerId,
        Guid secretId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        [FromBody] UpdateSecretRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSecretCommand(
            secretId,
            ifMatch ?? string.Empty,
            body.Type,
            body.Name,
            body.Username,
            body.Url,
            body.Tags,
            body.ReplaceValue,
            body.Value,
            body.ReplaceNotes,
            body.Notes,
            ownerId);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevealSharedSecretAsync(
        Guid ownerId,
        Guid secretId,
        [FromBody] RevealSecretRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new RevealSecretFieldCommand(secretId, body.Field, body.Intent, ownerId);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSharedFolderAsync(
        Guid ownerId,
        Guid folderId,
        [FromQuery(Name = ApiQuery.PageSize)] int? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetSharedFolderQuery(ownerId, folderId, pageSize ?? ListSharedItemsQuery.DefaultPageSize, continuationToken);
        var result = await mediator.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> FileSharedSecretAsync(
        Guid ownerId,
        Guid secretId,
        [FromBody] FileSharedItemRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new FileSharedItemCommand(ownerId, ShareItemKind.Secret, secretId, body.FolderId);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static async Task<IResult> FileSharedFolderAsync(
        Guid ownerId,
        Guid folderId,
        [FromBody] FileSharedItemRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new FileSharedItemCommand(ownerId, ShareItemKind.Folder, folderId, body.FolderId);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToNoContentHttpResult();
    }
}
