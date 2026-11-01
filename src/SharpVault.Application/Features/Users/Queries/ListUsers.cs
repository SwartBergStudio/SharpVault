// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mapster;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Dtos;

namespace SharpVault.Application.Features.Users.Queries;

public sealed record ListUsersQuery(int PageSize, string? ContinuationToken) : IRequest<Result<UserPageDto>>
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;
}

internal static class ListUsersMessages
{
    public const string PageSizeOutOfRange = "PageSize must be between 1 and 100.";

    public const string InvalidContinuationToken = "ContinuationToken is not valid.";
}

public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    private const int CanonicalGuidLength = 36;

    public ListUsersQueryValidator()
    {
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ListUsersQuery.MaxPageSize)
            .WithMessage(ListUsersMessages.PageSizeOutOfRange);

        RuleFor(x => x.ContinuationToken)
            .Must(IsCanonicalGuid)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(ListUsersMessages.InvalidContinuationToken);
    }

    private static bool IsCanonicalGuid(string? value) =>
        value is { Length: CanonicalGuidLength } && Guid.TryParseExact(value, "D", out _);
}

public sealed class ListUsersQueryHandler(IUserStore users)
    : IRequestHandler<ListUsersQuery, Result<UserPageDto>>
{
    public async Task<Result<UserPageDto>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        Guid? after = request.ContinuationToken is null ? null : Guid.ParseExact(request.ContinuationToken, "D");

        var page = await users.ListAsync(request.PageSize, after, cancellationToken);
        var items = page.Users.Adapt<List<UserSummaryDto>>();

        return Result<UserPageDto>.Success(new UserPageDto(items, page.NextAfterUserId?.ToString("D")));
    }
}
