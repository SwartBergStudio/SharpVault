// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Vault.History.Commands;
using SharpVault.Application.Features.Vault.History.Queries;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;

namespace SharpVault.Api.Endpoints.Vault;

public static class VaultHistoryEndpoints
{
    public static IEndpointRouteBuilder MapVaultHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Vault");

        group.MapGet(ApiRoutes.VaultSecretHistory, ListVersionsAsync)
            .WithName("Vault_ListSecretVersions")
            .WithSummary("List the previous values of a secret.")
            .WithDescription("Returns the version ids, replacement times and revisions of earlier values. No values are returned.")
            .Produces<SecretVersionListDto>()
            .ProducesVaultProblems(itemRoute: true);

        group.MapPost(ApiRoutes.VaultSecretVersionReveal, RevealVersionAsync)
            .RequireRateLimiting(RateLimitPolicies.Reveal)
            .WithName("Vault_RevealSecretVersion")
            .WithSummary("Reveal a previous value.")
            .WithDescription("Decrypts an earlier value of a secret and writes an audit entry before returning it.")
            .Produces<RevealedValueDto>()
            .ProducesVaultProblems(itemRoute: true)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(ApiRoutes.VaultSecretVersionRestore, RestoreVersionAsync)
            .WithName("Vault_RestoreSecretVersion")
            .WithSummary("Restore a previous value.")
            .WithDescription("Makes an earlier value current again. The If-Match header carries the secret's concurrency token.")
            .Produces<SecretDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> ListVersionsAsync(Guid secretId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListSecretVersionsQuery(secretId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevealVersionAsync(
        Guid secretId,
        Guid versionId,
        [FromBody] RevealVersionRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RevealSecretVersionCommand(secretId, versionId, body.Intent), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RestoreVersionAsync(
        Guid secretId,
        Guid versionId,
        [FromHeader(Name = VaultHeaders.IfMatch)] string? ifMatch,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RestoreSecretVersionCommand(secretId, versionId, ifMatch ?? string.Empty), cancellationToken);
        return result.ToHttpResult();
    }
}
