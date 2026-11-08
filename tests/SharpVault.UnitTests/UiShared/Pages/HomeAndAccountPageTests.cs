// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Pages;

public sealed class HomeAndAccountPageTests : ShellTestContext
{
    public HomeAndAccountPageTests()
    {
        SetRendererInfo(new RendererInfo("Server", true));
    }

    private IRenderedComponent<Home> RenderHome() => Render<Home>();

    private BunitNavigationManager Navigation => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private static CurrentUserDto UserDto(string name, bool isAdmin) =>
        new() { UserId = Guid.NewGuid(), DisplayName = name, IsAdmin = isAdmin };

    [Fact]
    public void HomePage_SignedOut_ShowsSignInLinkAndNoGreeting()
    {
        var cut = RenderHome();

        var signIn = cut.Find("a#home-sign-in");
        Assert.Equal(AccountRoutes.SignIn, signIn.GetAttribute("href"));
        Assert.Empty(cut.FindAll("#home-greeting"));
        Assert.Empty(cut.FindAll("#home-role"));
    }

    [Fact]
    public void HomePage_SignedIn_ReplacesTheHistoryEntryWithTheVault()
    {
        Authorization.SetAuthorized(DisplayName);

        RenderHome();

        var entry = Assert.Single(Navigation.History);
        Assert.Equal(VaultRoutes.Vault, entry.Uri);
        Assert.True(entry.Options.ReplaceHistoryEntry);
    }

    [Fact]
    public void HomePage_SignedIn_RendersNoHeroAndNoSignedInContent()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderHome();

        Assert.Empty(cut.FindAll("#home"));
        Assert.Empty(cut.FindAll("#home-greeting"));
        Assert.Empty(cut.FindAll("#home-role"));
        Assert.Empty(cut.FindAll("#home-open-vault"));
        Assert.Empty(cut.FindAll("#home-account-link"));
    }

    [Fact]
    public void HomePage_SignedOut_ShowsHeroAndNoNavigation()
    {
        var cut = RenderHome();

        Assert.NotNull(cut.Find("#home"));
        Assert.Empty(Navigation.History);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void HomePage_SignedOut_OffersNoOpenVaultLink()
    {
        var cut = RenderHome();

        Assert.Empty(cut.FindAll("#home-open-vault"));
        Assert.DoesNotContain(VaultRoutes.Vault, cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AccountPage_AdminBadge_ShownOnlyForAdministrators(bool isAdmin)
    {
        ReturnsCurrentUser(UserDto(DisplayName, isAdmin));

        var cut = Render<Account>();

        Assert.NotNull(cut.Find("#account-section"));
        Assert.Equal(isAdmin, cut.FindAll("#account-admin-badge").Count == 1);
    }

    [Fact]
    public void AccountPage_SignInRequired_NavigatesWithReturnUrlAccount()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("account");
        ReturnsCurrentUserStatus(ApiResponse<CurrentUserDto>.SignInRequired());

        var cut = Render<Account>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        var expected = $"http://localhost{AccountRoutes.SignIn}?returnUrl={Uri.EscapeDataString("/account?signinRetry=1")}";
        Assert.Equal(expected, navigation.Uri);
        Assert.Empty(cut.FindAll("#account-section"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountPage_AccessDenied_NavigatesToAccessDenied()
    {
        ReturnsCurrentUserStatus(ApiResponse<CurrentUserDto>.AccessDenied());

        var cut = Render<Account>();

        Assert.Equal($"http://localhost{AccountRoutes.AccessDenied}", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Empty(cut.FindAll("#account-section"));
        Accessor.Received().Invalidate();
    }
}
