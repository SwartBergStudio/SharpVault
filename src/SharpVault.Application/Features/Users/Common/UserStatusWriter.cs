// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Features.Users.Common;

public sealed class UserStatusWriter(IUserStore users, IUserAccessCache cache)
{
    private const int MaxAttempts = 3;

    public async Task<Result<UserSummaryDto>> SetStatusAsync(
        Guid userId,
        UserStatus target,
        string alreadyInStatusMessage,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var stored = await users.GetAsync(userId, cancellationToken);
            if (stored is null)
            {
                return Result<UserSummaryDto>.NotFound();
            }

            if (stored.Value.Status == target)
            {
                return Result<UserSummaryDto>.Rejected(alreadyInStatusMessage);
            }

            var changed = stored.Value;
            changed.Status = target;
            var write = await users.ReplaceAsync(changed, stored.ETag, cancellationToken);
            if (write == StoreWriteResult.Written)
            {
                cache.Invalidate(userId);
                return Result<UserSummaryDto>.Success(changed.Adapt<UserSummaryDto>());
            }

            if (write == StoreWriteResult.NotFound)
            {
                return Result<UserSummaryDto>.NotFound();
            }
        }

        return Result<UserSummaryDto>.Conflict();
    }
}
