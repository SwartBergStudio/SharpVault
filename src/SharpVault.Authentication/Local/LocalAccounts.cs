// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using SharpVault.Authentication.Identity;

namespace SharpVault.Authentication.Local;

public static class LocalAccounts
{
    private static readonly string[] AdminRoles = [AppRoles.Admin, AppRoles.User];
    private static readonly string[] UserRoles = [AppRoles.User];

    public static LocalAccount AdminOne { get; } =
        new(Guid.Parse("5a1d0000-0000-4000-8000-000000000001"), "Admin One", "admin.one@sharpvault.invalid", AdminRoles);

    public static LocalAccount AdminTwo { get; } =
        new(Guid.Parse("5a1d0000-0000-4000-8000-000000000002"), "Admin Two", "admin.two@sharpvault.invalid", AdminRoles);

    public static LocalAccount UserOne { get; } =
        new(Guid.Parse("5a1d0000-0000-4000-8000-000000000003"), "User One", "user.one@sharpvault.invalid", UserRoles);

    public static LocalAccount UserTwo { get; } =
        new(Guid.Parse("5a1d0000-0000-4000-8000-000000000004"), "User Two", "user.two@sharpvault.invalid", UserRoles);

    public static IReadOnlyList<LocalAccount> All { get; } = [AdminOne, AdminTwo, UserOne, UserTwo];

    public static bool TryFind(Guid id, [NotNullWhen(true)] out LocalAccount? account)
    {
        account = All.FirstOrDefault(candidate => candidate.Id == id);
        return account is not null;
    }
}
