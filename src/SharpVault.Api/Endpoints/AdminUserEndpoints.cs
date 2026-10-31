// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Api.Errors;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Users.Dtos;
using SharpVault.Application.Features.Users.Queries;

namespace SharpVault.Api.Endpoints;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .RequireAuthorization(Policies.Admin)
            .WithTags("Admin users");

        group.MapGet(string.Empty, ListUsersAsync)
            .WithName("AdminUsers_List")
            .WithSummary("List users.")
            .WithDescription("Returns a page of users with directory data only: id, display name, email, status and last sign-in. No secrets, shares or vault contents are returned.")
            .Produces<UserPageDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListUsersAsync(
        int? pageSize,
        string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListUsersQuery(pageSize ?? ListUsersQuery.DefaultPageSize, continuationToken);
        var result = await mediator.Send(query, cancellationToken);
        return result.ToHttpResult();
    }
}
