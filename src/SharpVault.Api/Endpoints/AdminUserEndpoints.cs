// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Commands;
using SharpVault.Application.Features.Users.Queries;
using SharpVault.Dtos.Users;
using SharpVault.Dtos;

namespace SharpVault.Api.Endpoints;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty)
            .RequireAuthorization(Policies.Admin)
            .WithTags("Admin users");

        group.MapGet(ApiRoutes.AdminUsers, ListUsersAsync)
            .WithName("AdminUsers_List")
            .WithSummary("List users.")
            .WithDescription("Returns a page of users with directory data only: id, display name, email, status and last sign-in. No secrets, shares or vault contents are returned.")
            .Produces<UserPageDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet(ApiRoutes.AdminUserCounts, GetUserCountsAsync)
            .RequireRateLimiting(RateLimitPolicies.AdminActions)
            .WithName("AdminUsers_Counts")
            .WithSummary("Get a user's secret and share counts.")
            .WithDescription("Returns how many secrets the user holds and how many shares they have given. Numbers only: no names, values or folder names.")
            .Produces<UserCountsDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: false);

        group.MapPost(ApiRoutes.AdminUserLock, LockUserAsync)
            .RequireRateLimiting(RateLimitPolicies.AdminActions)
            .WithName("AdminUsers_Lock")
            .WithSummary("Lock a user.")
            .WithDescription("Locks a user with a reason category and a reason. The user is refused on the next request. Administrators cannot lock themselves.")
            .Produces<UserSummaryDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        group.MapPost(ApiRoutes.AdminUserUnlock, UnlockUserAsync)
            .RequireRateLimiting(RateLimitPolicies.AdminActions)
            .WithName("AdminUsers_Unlock")
            .WithSummary("Unlock a user.")
            .WithDescription("Unlocks a user with a reason category and a reason.")
            .Produces<UserSummaryDto>()
            .ProducesVaultProblems(itemRoute: true, concurrent: true);

        return app;
    }

    private static async Task<IResult> GetUserCountsAsync(Guid userId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetUserCountsQuery(userId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> LockUserAsync(
        Guid userId,
        [FromBody] LockUserRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new LockUserCommand(userId, TransferEnumMap.ToDomain(body.Category), body.Reason);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UnlockUserAsync(
        Guid userId,
        [FromBody] LockUserRequestDto body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new UnlockUserCommand(userId, TransferEnumMap.ToDomain(body.Category), body.Reason);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ListUsersAsync(
        [FromQuery(Name = ApiQuery.PageSize)] string? pageSize,
        [FromQuery(Name = ApiQuery.ContinuationToken)] string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var size = TryParsePageSize(pageSize, out var parsed) ? parsed : ListUsersQuery.UnparsedPageSize;
        var query = new ListUsersQuery(size, continuationToken);
        var result = await mediator.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static bool TryParsePageSize(string? value, out int size)
    {
        if (value is null)
        {
            size = ListUsersQuery.DefaultPageSize;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out size);
    }
}
