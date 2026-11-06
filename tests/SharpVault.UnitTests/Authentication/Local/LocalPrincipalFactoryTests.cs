// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;

namespace SharpVault.UnitTests.Authentication.Local;

public sealed class LocalPrincipalFactoryTests
{
    private const string SchemeName = "TestScheme";

    [Fact]
    public void Create_Account_CarriesEveryClaim()
    {
        var account = LocalAccounts.UserOne;

        var principal = LocalPrincipalFactory.Create(account, SchemeName);

        Assert.Equal(account.Id.ToString("D"), principal.FindFirst("oid")?.Value);
        Assert.Equal(LocalAuthenticationDefaults.TenantId, principal.FindFirst("tid")?.Value);
        Assert.Equal(account.DisplayName, principal.FindFirst("name")?.Value);
        Assert.Equal(account.Email, principal.FindFirst("preferred_username")?.Value);
        Assert.Equal(account.Email, principal.FindFirst("email")?.Value);
        Assert.Equal("access_as_user", principal.FindFirst("scp")?.Value);
        Assert.Equal(account.DisplayName, principal.Identity?.Name);
    }

    [Fact]
    public void Create_AdminOne_IsInAdminRole()
    {
        var principal = LocalPrincipalFactory.Create(LocalAccounts.AdminOne, SchemeName);

        Assert.True(principal.IsInRole(AppRoles.Admin));
        Assert.True(principal.IsInRole(AppRoles.User));
    }

    [Fact]
    public void Create_UserOne_IsNotInAdminRole()
    {
        Assert.False(LocalPrincipalFactory.Create(LocalAccounts.UserOne, SchemeName).IsInRole(AppRoles.Admin));
    }

    [Fact]
    public void Create_Always_SetsAccountTypeToMember()
    {
        Assert.Equal("0", LocalPrincipalFactory.Create(LocalAccounts.UserOne, SchemeName).FindFirst("acct")?.Value);
    }

    [Fact]
    public void Create_Always_UsesTheSuppliedAuthenticationType()
    {
        var principal = LocalPrincipalFactory.Create(LocalAccounts.UserOne, SchemeName);

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal(SchemeName, principal.Identity?.AuthenticationType);
    }
}
