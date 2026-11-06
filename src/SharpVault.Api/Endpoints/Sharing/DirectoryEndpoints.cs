// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Sharing.Directory;
using SharpVault.Dtos;
using SharpVault.Dtos.Directory;

namespace SharpVault.Api.Endpoints.Sharing;

public static class DirectoryEndpoints
{
    public static IEndpointRouteBuilder MapDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.DirectorySearch)
            .WithTags("Sharing");

        group.MapGet(ApiRoutes.DirectoryUsers, SearchUsersAsync)
            .WithName("Directory_SearchUsers")
            .WithSummary("Search for people to share with.")
            .WithDescription("Returns a short list of tenant members whose name or email matches the search text. The search text is required.")
            .Produces<DirectoryUserListDto>()
            .ProducesVaultProblems(itemRoute: false);

        return app;
    }

    private static async Task<IResult> SearchUsersAsync(
        [FromQuery(Name = ApiQuery.Search)] string? search,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SearchDirectoryUsersQuery(search ?? string.Empty), cancellationToken);
        return result.ToHttpResult();
    }
}
