// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Directory;

namespace SharpVault.Infrastructure.Directory;

public sealed class TableUserDirectory(IUserStore users) : IUserDirectory
{
    private const int PageSize = 100;

    public async Task<IReadOnlyList<DirectoryUser>> SearchAsync(
        string term,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(term);

        var matches = new List<DirectoryUser>();
        var scanned = 0;
        Guid? after = null;

        while (scanned < DirectorySearchLimits.MaxRowsScanned && matches.Count < limit)
        {
            var page = await users.ListAsync(PageSize, after, cancellationToken);
            foreach (var user in page.Users)
            {
                scanned++;
                if (Matches(user.DisplayName, term) || Matches(user.Email, term))
                {
                    matches.Add(new DirectoryUser(user.Id, user.DisplayName, user.Email));
                    if (matches.Count >= limit)
                    {
                        break;
                    }
                }
            }

            if (page.NextAfterUserId is null)
            {
                break;
            }

            after = page.NextAfterUserId;
        }

        return matches;
    }

    public async Task<DirectoryUser?> FindMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await users.GetAsync(userId, cancellationToken);
        return found is null ? null : new DirectoryUser(found.Value.Id, found.Value.DisplayName, found.Value.Email);
    }

    private static bool Matches(string? candidate, string term) =>
        candidate is not null && candidate.Contains(term, StringComparison.OrdinalIgnoreCase);
}
