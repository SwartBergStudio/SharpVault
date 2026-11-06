// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;

namespace SharpVault.UnitTests.Authentication.Local;

public sealed class LocalAccountsTests
{
    [Fact]
    public void All_Always_HasFourDistinctCanonicalIdsAndNames()
    {
        Assert.Equal(4, LocalAccounts.All.Count);
        Assert.Equal(4, LocalAccounts.All.Select(account => account.Id).Distinct().Count());
        Assert.Equal(4, LocalAccounts.All.Select(account => account.DisplayName).Distinct().Count());
        Assert.All(LocalAccounts.All, account => Assert.NotEqual(Guid.Empty, account.Id));
        Assert.Equal(["Admin One", "Admin Two", "User One", "User Two"], LocalAccounts.All.Select(account => account.DisplayName));
    }

    [Fact]
    public void Admins_Always_HaveAdminAndUserRoles()
    {
        foreach (var admin in new[] { LocalAccounts.AdminOne, LocalAccounts.AdminTwo })
        {
            Assert.Contains(AppRoles.Admin, admin.Roles);
            Assert.Contains(AppRoles.User, admin.Roles);
        }
    }

    [Fact]
    public void Users_Always_HaveOnlyUserRole()
    {
        foreach (var user in new[] { LocalAccounts.UserOne, LocalAccounts.UserTwo })
        {
            Assert.Equal([AppRoles.User], user.Roles);
        }
    }

    [Fact]
    public void TryFind_UnknownId_ReturnsFalse()
    {
        Assert.False(LocalAccounts.TryFind(Guid.NewGuid(), out var account));
        Assert.Null(account);
    }

    [Fact]
    public void TryFind_KnownId_ReturnsAccount()
    {
        Assert.True(LocalAccounts.TryFind(LocalAccounts.UserTwo.Id, out var account));
        Assert.Same(LocalAccounts.UserTwo, account);
    }

    [Fact]
    public void Emails_Always_UseInvalidTld()
    {
        Assert.All(LocalAccounts.All, account => Assert.EndsWith("@sharpvault.invalid", account.Email, StringComparison.Ordinal));
    }
}
