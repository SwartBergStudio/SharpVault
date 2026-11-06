// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Users;

namespace SharpVault.Application.Features.Users.Common;

public sealed class AdminActorGuard(ICurrentUser currentUser, IUserStore users)
{
    public async Task<AdminActor?> RequireAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdmin)
        {
            return null;
        }

        var stored = await users.GetAsync(currentUser.UserId, cancellationToken);
        if (stored is not { Value: { Status: UserStatus.Active, IsAdmin: true } row })
        {
            return null;
        }

        return new AdminActor(row.Id, row.DisplayName);
    }
}
