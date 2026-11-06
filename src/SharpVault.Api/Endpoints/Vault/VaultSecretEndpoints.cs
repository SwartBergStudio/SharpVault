// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Application.Features.Vault.Secrets.Queries;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;

namespace SharpVault.Api.Endpoints.Vault;

public static class VaultSecretEndpoints
{
    public static IEndpointRouteBuilder MapVaultSecretEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Vault");

        group.MapGet(ApiRoutes.VaultSecrets, ListSecretsAsync)
            .WithName("Vault_ListSecrets")
            .WithSummary("List secrets.")
            .WithDescription("Returns a page of the caller's secrets with metadata only. No value or notes are returned.")
            .Produces<SecretPageDto>()
            .ProducesVaultProblems(itemRoute: false);

        group.MapGet(ApiRoutes.VaultSecret, GetSecretAsync)
            .WithName("Vault_GetSecret")
            .WithSummary("Get a secret.")
            .WithDescription("Returns the metadata of one secret. No value or notes are returned.")
            .Produces<SecretDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPost(ApiRoutes.VaultSecrets, CreateSecretAsync)
            .WithName("Vault_CreateSecret")
            .WithSummary("Create a secret.")
            .WithDescription("Creates a secret in the caller's vault, optionally inside a folder.")
            .Produces<SecretDto>(StatusCodes.Status201Created)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPut(ApiRoutes.VaultSecret, UpdateSecretAsync)
            .WithName("Vault_UpdateSecret")
            .WithSummary("Update a secret.")
            .WithDescription("Updates a secret. The If-Match header carries the concurrency token from the last response.")
            .Produces<SecretDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.VaultSecretMove, MoveSecretAsync)
            .WithName("Vault_MoveSecret")
            .WithSummary("Move a secret.")
            .WithDescription("Moves a secret into a folder or to the top level. The If-Match header carries the concurrency token.")
            .Produces<SecretDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapDelete(ApiRoutes.VaultSecret, DeleteSecretAsync)
            .WithName("Vault_DeleteSecret")
            .WithSummary("Delete a secret.")
            .WithDescription("Moves a secret to the recycle bin. The If-Match header carries the concurrency token.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.VaultSecretReveal, RevealSecretAsync)
            .RequireRateLimiting(RateLimitPolicies.Reveal)
            .WithName("Vault_RevealSecret")
            .WithSummary("Reveal a secret value or notes.")
            .WithDescription("Decrypts the value or the notes of a secret and writes an audit entry before returning it.")
            .Produces<RevealedValueDto>()
            .ProducesVaultProblems(itemRoute: true)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> ListSecretsAsync(
        [FromQuery(Name = ApiQuery.FolderId)] Guid? folderId,
        [FromQuery(Name = ApiQuery.Unfiled)] bool? unfiled,
        [FromQuery(Name = ApiQuery.Search)] string? search,
        [FromQuery(Name = ApiQuery.PageSize)] int? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListSecretsQuery(folderId, unfiled ?? false, search, pageSize ?? VaultLimits.DefaultPageSize, continuationToken);
        var result = await mediator.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSecretAsync(Guid secretId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetSecretQuery(secretId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateSecretAsync(
        [FromBody] CreateSecretRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new CreateSecretCommand(body.Type, body.Name, body.Username, body.Url, body.Value, body.Notes, body.Tags, body.FolderId);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToCreatedHttpResult(secret => $"{ApiRoutes.VaultSecrets}/{secret.Id:D}");
    }

    private static async Task<IResult> UpdateSecretAsync(
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
            body.Notes);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> MoveSecretAsync(
        Guid secretId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        [FromBody] MoveSecretRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new MoveSecretCommand(secretId, ifMatch ?? string.Empty, body.FolderId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteSecretAsync(
        Guid secretId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeleteSecretCommand(secretId, ifMatch ?? string.Empty), cancellationToken);
        return result.ToNoContentHttpResult();
    }

    private static async Task<IResult> RevealSecretAsync(
        Guid secretId,
        [FromBody] RevealSecretRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RevealSecretFieldCommand(secretId, body.Field, body.Intent), cancellationToken);
        return result.ToHttpResult();
    }
}
