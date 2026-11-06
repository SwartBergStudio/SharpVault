// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Directory;

namespace SharpVault.Application.Features.Sharing.Directory;

public sealed record SearchDirectoryUsersQuery(string Term) : IRequest<Result<DirectoryUserListDto>>
{
    public override string ToString() => nameof(SearchDirectoryUsersQuery);
}

internal static class SearchDirectoryUsersMessages
{
    public const string TermRequired = "Search term is required.";

    public const string TermLength = "Search term must be between 3 and 100 characters.";

    public const string TermCharacters = "Search term contains characters that are not allowed.";
}

public sealed partial class SearchDirectoryUsersQueryValidator : AbstractValidator<SearchDirectoryUsersQuery>
{
    public SearchDirectoryUsersQueryValidator()
    {
        RuleFor(x => x.Term)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(SearchDirectoryUsersMessages.TermRequired)
            .Must(term => term.Trim().Length >= DirectorySearchLimits.MinTermLength
                && term.Trim().Length <= DirectorySearchLimits.MaxTermLength)
            .WithMessage(SearchDirectoryUsersMessages.TermLength)
            .Must(term => AllowedTerm().IsMatch(term.Trim()))
            .WithMessage(SearchDirectoryUsersMessages.TermCharacters);
    }

    [GeneratedRegex(@"^[\p{L}\p{M}\p{Nd} .'@_\-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedTerm();
}

public sealed class SearchDirectoryUsersQueryHandler(
    IUserDirectory directory,
    IUserStore users,
    ICurrentUser currentUser)
    : IRequestHandler<SearchDirectoryUsersQuery, Result<DirectoryUserListDto>>
{
    public async Task<Result<DirectoryUserListDto>> Handle(
        SearchDirectoryUsersQuery request,
        CancellationToken cancellationToken)
    {
        var found = await directory.SearchAsync(
            request.Term.Trim(),
            DirectorySearchLimits.ResultLimit + 1,
            cancellationToken);

        var candidates = found.Where(user => user.Id != currentUser.UserId).ToList();
        var rows = new Versioned<User>?[candidates.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = DirectorySearchLimits.StatusParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            options,
            async (index, token) => rows[index] = await users.GetAsync(candidates[index].Id, token));

        var items = new List<DirectoryUserDto>();
        for (var index = 0; index < candidates.Count && items.Count < DirectorySearchLimits.ResultLimit; index++)
        {
            if (rows[index] is { Value.Status: not UserStatus.Active })
            {
                continue;
            }

            var user = candidates[index];
            items.Add(new DirectoryUserDto { Id = user.Id, DisplayName = user.DisplayName, Email = user.Email });
        }

        return Result<DirectoryUserListDto>.Success(new DirectoryUserListDto { Items = items });
    }
}
