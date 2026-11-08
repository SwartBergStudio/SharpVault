// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Layout;

public sealed class SideNavTests : ShellTestContext
{
    private IRenderedComponent<SideNav> RenderNav(CurrentUserDto? currentUser) =>
        Render<SideNav>(parameters =>
        {
            parameters.Add(nav => nav.Open, true);
            if (currentUser is not null)
            {
                parameters.AddCascadingValue(LayoutCascades.CurrentUser, currentUser);
            }
        });

    private static CurrentUserDto UserDto(bool isAdmin) =>
        new() { UserId = Guid.NewGuid(), DisplayName = DisplayName, IsAdmin = isAdmin };

    [Fact]
    public void SideNav_SignedOut_RendersNothing()
    {
        var cut = RenderNav(null);

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup), $"Signed-out side nav rendered: {cut.Markup}");
        Assert.Empty(cut.FindAll("#side-nav"));
    }

    [Fact]
    public void SideNav_User_ShowsVaultAndAccountLinksOnly()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderNav(UserDto(isAdmin: false));

        var ids = cut.FindAll("#side-nav a").Select(link => link.Id).ToList();
        Assert.Equal(["nav-account", "nav-one-time-links", "nav-recycle-bin", "nav-shared", "nav-vault"], ids.Order(StringComparer.Ordinal));
        Assert.Empty(cut.FindAll("#nav-admin-users"));
        Assert.Empty(cut.FindAll("#nav-admin-heading"));
    }

    [Fact]
    public void SideNav_Admin_ShowsAdminUsersLink()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderNav(UserDto(isAdmin: true));

        var ids = cut.FindAll("#side-nav a").Select(link => link.Id).ToList();
        Assert.Equal(["nav-account", "nav-admin-transfers", "nav-admin-users", "nav-one-time-links", "nav-recycle-bin", "nav-shared", "nav-vault"], ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SideNav_CookiePrincipalClaimsAdminButApiSaysUser_HidesAdminLink()
    {
        Authorization.SetAuthorized(DisplayName);
        Authorization.SetRoles("SharpVault.Admin");
        Authorization.SetClaims(new Claim("roles", "SharpVault.Admin"));

        var cut = RenderNav(UserDto(isAdmin: false));

        Assert.Empty(cut.FindAll("#nav-admin-users"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SideNav_CurrentUserLoadFails_HidesAdminLink()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderNav(null);

        Assert.Equal(["nav-account", "nav-one-time-links", "nav-recycle-bin", "nav-shared", "nav-vault"], cut.FindAll("#side-nav a").Select(link => link.Id).Order(StringComparer.Ordinal));
        Assert.Empty(cut.FindAll("#nav-admin-users"));
    }

    [Fact]
    public void SideNav_Links_UseTheAccountRoutesConstants()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderNav(UserDto(isAdmin: true));

        Assert.Equal(AccountRoutes.Account, cut.Find("#nav-account").GetAttribute("href"));
    }

    [Fact]
    public void SideNav_VaultLinks_UseTheVaultRoutesConstants()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderNav(UserDto(isAdmin: false));

        Assert.Equal(VaultRoutes.Vault, cut.Find("#nav-vault").GetAttribute("href"));
        Assert.Equal(VaultRoutes.RecycleBin, cut.Find("#nav-recycle-bin").GetAttribute("href"));
    }

    [Fact]
    public void SideNav_AdminLink_UsesAccountRoutesAdminUsers()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderNav(UserDto(isAdmin: true));

        Assert.Equal(AccountRoutes.AdminUsers, cut.Find("#nav-admin-users").GetAttribute("href"));
    }

    [Fact]
    public void SideNav_SignedIn_NavigationIsLabelledAndEveryLinkHasIdAndText()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderNav(UserDto(isAdmin: true));

        Assert.Equal("Main", cut.Find("#side-nav").GetAttribute("aria-label"));
        Assert.All(cut.FindAll("#side-nav a"), link =>
        {
            Assert.False(string.IsNullOrWhiteSpace(link.Id));
            Assert.False(string.IsNullOrWhiteSpace(link.TextContent));
        });
    }

}
