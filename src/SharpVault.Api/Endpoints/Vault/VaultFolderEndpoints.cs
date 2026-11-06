// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Vault.Folders.Commands;
using SharpVault.Application.Features.Vault.Folders.Queries;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;

namespace SharpVault.Api.Endpoints.Vault;

public static class VaultFolderEndpoints
{
    public static IEndpointRouteBuilder MapVaultFolderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Vault");

        group.MapGet(ApiRoutes.VaultFolders, ListFoldersAsync)
            .WithName("Vault_ListFolders")
            .WithSummary("List folders.")
            .WithDescription("Returns every folder in the caller's vault.")
            .Produces<FolderListDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapPost(ApiRoutes.VaultFolders, CreateFolderAsync)
            .WithName("Vault_CreateFolder")
            .WithSummary("Create a folder.")
            .WithDescription("Creates a folder at the top level or inside another folder.")
            .Produces<FolderDto>(StatusCodes.Status201Created)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPut(ApiRoutes.VaultFolder, RenameFolderAsync)
            .WithName("Vault_RenameFolder")
            .WithSummary("Rename a folder.")
            .WithDescription("Changes the name of a folder.")
            .Produces<FolderDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.VaultFolderMove, MoveFolderAsync)
            .WithName("Vault_MoveFolder")
            .WithSummary("Move a folder.")
            .WithDescription("Moves a folder to the top level or inside another folder.")
            .Produces<FolderDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapDelete(ApiRoutes.VaultFolder, DeleteFolderAsync)
            .WithName("Vault_DeleteFolder")
            .WithSummary("Delete a folder.")
            .WithDescription("Deletes a folder. The mode query parameter is MoveToParent or DeleteContents.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        return app;
    }

    private static async Task<IResult> ListFoldersAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListFoldersQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateFolderAsync(
        [FromBody] CreateFolderRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CreateFolderCommand(body.Name, body.ParentId), cancellationToken);
        return result.ToCreatedHttpResult(folder => $"{ApiRoutes.VaultFolders}/{folder.Id:D}");
    }

    private static async Task<IResult> RenameFolderAsync(
        Guid folderId,
        [FromBody] RenameFolderRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RenameFolderCommand(folderId, body.Name), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> MoveFolderAsync(
        Guid folderId,
        [FromBody] MoveFolderRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new MoveFolderCommand(folderId, body.ParentId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteFolderAsync(
        Guid folderId,
        [FromQuery(Name = ApiQuery.Mode)] string mode,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeleteFolderCommand(folderId, VaultQueryParser.ParseMode(mode)), cancellationToken);
        return result.ToNoContentHttpResult();
    }
}
