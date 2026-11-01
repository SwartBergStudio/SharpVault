// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Api.Errors;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Users.Dtos;
using SharpVault.Application.Features.Users.Queries;

namespace SharpVault.Api.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me")
            .RequireAuthorization(Policies.User)
            .WithTags("Me");

        group.MapGet(string.Empty, GetCurrentUserAsync)
            .WithName("Me_Get")
            .WithSummary("Get the signed-in user.")
            .WithDescription("Returns the signed-in user's id, display name and whether they are an admin.")
            .Produces<CurrentUserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetCurrentUserAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCurrentUserQuery(), cancellationToken);
        return result.ToHttpResult();
    }
}
