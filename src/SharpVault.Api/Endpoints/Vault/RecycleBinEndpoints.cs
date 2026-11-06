// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin.Commands;
using SharpVault.Application.Features.Vault.RecycleBin.Queries;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;

namespace SharpVault.Api.Endpoints.Vault;

public static class RecycleBinEndpoints
{
    public static IEndpointRouteBuilder MapRecycleBinEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Vault");

        group.MapGet(ApiRoutes.RecycleBin, ListRecycleBinAsync)
            .WithName("Vault_ListRecycleBin")
            .WithSummary("List the recycle bin.")
            .WithDescription("Returns a page of the caller's deleted secrets and folders.")
            .Produces<RecycleBinPageDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapPost(ApiRoutes.RecycleBinSecretRestore, RestoreSecretAsync)
            .WithName("Vault_RestoreRecycledSecret")
            .WithSummary("Restore a deleted secret.")
            .WithDescription("Moves a secret out of the recycle bin.")
            .Produces<SecretDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.RecycleBinFolderRestore, RestoreFolderAsync)
            .WithName("Vault_RestoreRecycledFolder")
            .WithSummary("Restore a deleted folder.")
            .WithDescription("Moves a folder and what was deleted with it out of the recycle bin.")
            .Produces<FolderDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapDelete(ApiRoutes.RecycleBinSecret, PurgeSecretAsync)
            .WithName("Vault_PurgeRecycledSecret")
            .WithSummary("Permanently delete a secret.")
            .WithDescription("Erases a secret from the recycle bin. This cannot be undone.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapDelete(ApiRoutes.RecycleBinFolder, PurgeFolderAsync)
            .WithName("Vault_PurgeRecycledFolder")
            .WithSummary("Permanently delete a folder.")
            .WithDescription("Erases a folder and what was deleted with it from the recycle bin. This cannot be undone.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        return app;
    }

    private static async Task<IResult> ListRecycleBinAsync(
        [FromQuery(Name = ApiQuery.PageSize)] int? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListRecycleBinQuery(pageSize ?? VaultLimits.DefaultPageSize, continuationToken);
        var result = await mediator.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RestoreSecretAsync(Guid secretId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RestoreRecycledSecretCommand(secretId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RestoreFolderAsync(Guid folderId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RestoreRecycledFolderCommand(folderId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> PurgeSecretAsync(Guid secretId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new PurgeRecycledSecretCommand(secretId), cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static async Task<IResult> PurgeFolderAsync(Guid folderId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new PurgeRecycledFolderCommand(folderId), cancellationToken);
        return result.ToNoContentHttpResult();
    }
}
