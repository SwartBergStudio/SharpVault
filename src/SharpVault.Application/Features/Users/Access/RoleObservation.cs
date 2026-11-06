// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Users.Access;

public static class RoleObservation
{
    public static bool IsNewer(bool storedIsAdmin, DateTimeOffset? storedAt, bool tokenIsAdmin, DateTimeOffset? tokenAt)
    {
        if (tokenAt is not { } observed || storedIsAdmin == tokenIsAdmin)
        {
            return false;
        }

        if (storedAt is not { } recorded)
        {
            return true;
        }

        if (observed > recorded)
        {
            return true;
        }

        return observed == recorded && !tokenIsAdmin;
    }
}
