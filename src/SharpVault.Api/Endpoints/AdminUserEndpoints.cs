// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
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

    private const string PageSizeField = "PageSize";
    private const string PageSizeInvalid = "PageSize must be between 1 and 100.";

    private static async Task<IResult> ListUsersAsync(
        string? pageSize,
        string? continuationToken,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (!TryParsePageSize(pageSize, out var size))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [PageSizeField] = [PageSizeInvalid] });
        }

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
