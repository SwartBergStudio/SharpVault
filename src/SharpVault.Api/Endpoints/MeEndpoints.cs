// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Users.Queries;
using SharpVault.Application.Features.Users.Sessions;
using SharpVault.Dtos.Users;
using SharpVault.Dtos;

namespace SharpVault.Api.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiRoutes.Me)
            .RequireAuthorization(Policies.User)
            .WithTags("Me");

        group.MapGet(string.Empty, GetCurrentUserAsync)
            .WithName("Me_Get")
            .WithSummary("Get the signed-in user.")
            .WithDescription("Returns the signed-in user's id, display name and whether they are an admin.")
            .Produces<CurrentUserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        var sessions = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.User)
            .RequireRateLimiting(RateLimitPolicies.Vault)
            .WithTags("Me");

        sessions.MapPost(ApiRoutes.MeSessionCheck, CheckSessionAsync)
            .WithName("Me_SessionCheck")
            .WithSummary("Check a session of the signed-in user.")
            .WithDescription("Reports whether the session named in the body is still active. The session id travels in the body, never in the route.")
            .Produces<SessionStatusDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        sessions.MapPost(ApiRoutes.MeSessionRevoke, RevokeSessionAsync)
            .WithName("Me_SessionRevoke")
            .WithSummary("Revoke a session of the signed-in user.")
            .WithDescription("Ends the session named in the body on every instance. Only the signed-in user's own sessions can be revoked.")
            .Produces<SessionStatusDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetCurrentUserAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCurrentUserQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CheckSessionAsync(SessionReferenceRequestDto body, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CheckSessionQuery(body.SessionId, body.ExpiresAt), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeSessionAsync(SessionReferenceRequestDto body, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RevokeSessionCommand(body.SessionId, body.ExpiresAt), cancellationToken);
        return result.ToHttpResult();
    }
}
