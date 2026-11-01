// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Users;

namespace SharpVault.Infrastructure.Users;

internal static class UserStatusText
{
    private const string ActiveText = "Active";

    public static UserStatus Parse(string? value) =>
        string.Equals(value, ActiveText, StringComparison.Ordinal) ? UserStatus.Active : UserStatus.Locked;
}
