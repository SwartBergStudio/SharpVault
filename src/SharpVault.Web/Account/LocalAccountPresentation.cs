// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;

namespace SharpVault.Web.Account;

public static class LocalAccountPresentation
{
    public static string Slug(LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.DisplayName.ToLowerInvariant().Replace(' ', '-');
    }

    public static string RoleLabel(LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal) ? "Administrator" : "User";
    }
}
