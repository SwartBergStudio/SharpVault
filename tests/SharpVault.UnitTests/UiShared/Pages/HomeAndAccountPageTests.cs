// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Pages;

public sealed class HomeAndAccountPageTests : ShellTestContext
{
    private const string EntraSignInText = "Sign in with your work account";

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
    public void HomePage_SignedOutLocalMode_SignInButtonSaysSignIn()
    {
        var cut = RenderHome();

        Assert.Equal("Sign in", cut.Find("#home-sign-in").TextContent.Trim());
    }

    [Fact]
    public void HomePage_SignedOutEntraMode_SignInButtonSaysWorkAccount()
    {
        SignInOptions.HomeButtonText = EntraSignInText;

        var cut = RenderHome();

        Assert.Equal(EntraSignInText, cut.Find("#home-sign-in").TextContent.Trim());
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
    public void HomePage_SignedIn_AsAdmin_ReplacesTheHistoryEntryWithTheVault()
    {
        Authorization.SetAuthorized(DisplayName);
        ReturnsCurrentUser(UserDto(DisplayName, isAdmin: true));

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

    [Fact]
    [Trait("Category", "Security")]
    public void HomePage_SignedOut_ShowsNoUserData()
    {
        var cut = RenderHome();

        Assert.DoesNotContain("Signed in as", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Account_EmptyDisplayName_ShowsTheFallbackText()
    {
        ReturnsCurrentUser(UserDto(string.Empty, isAdmin: false));

        var cut = Render<Account>();

        Assert.Equal("No name on file", cut.Find("#account-display-name").TextContent);
    }

    [Fact]
    public void AccountPage_ProfileLoaded_RendersNameAndUserId()
    {
        var userId = Guid.NewGuid();
        ReturnsCurrentUser(new CurrentUserDto { UserId = userId, DisplayName = DisplayName, IsAdmin = false });

        var cut = Render<Account>();

        var name = cut.Find("#account-display-name");
        Assert.Equal(DisplayName, name.TextContent);
        Assert.Equal(0, name.ChildElementCount);
        var id = cut.Find("#account-user-id");
        Assert.Equal(userId.ToString(), id.TextContent);
        Assert.NotNull(cut.Find("#account-section"));
        Assert.Empty(cut.FindAll("#account-error"));
    }

    [Fact]
    public void AccountPage_Admin_RendersAdminBadge()
    {
        ReturnsCurrentUser(UserDto(DisplayName, isAdmin: true));

        var cut = Render<Account>();

        var badge = cut.Find("#account-admin-badge");
        Assert.Contains("Administrator", badge.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountPage_NonAdmin_RendersNoAdminBadge()
    {
        ReturnsCurrentUser(UserDto(DisplayName, isAdmin: false));

        var cut = Render<Account>();

        Assert.NotNull(cut.Find("#account-section"));
        Assert.Empty(cut.FindAll("#account-admin-badge"));
        Assert.DoesNotContain("Administrator", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountPage_NameWithMarkup_IsEncoded()
    {
        const string hostile = "<img src=x onerror=alert(1)>";
        ReturnsCurrentUser(UserDto(hostile, isAdmin: false));

        var cut = Render<Account>();

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal(hostile, cut.Find("#account-display-name").TextContent);
    }

    [Fact]
    public void AccountPage_ApiFailure_ShowsErrorStateWithRetry()
    {
        ReturnsCurrentUserStatus(ApiResponse<CurrentUserDto>.Failed());

        var cut = Render<Account>();

        var alert = cut.Find("#account-error");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains(LoadableComponentBase<CurrentUserDto>.GenericErrorText, alert.TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#account-retry"));
        Assert.Empty(cut.FindAll("#account-section"));
        Assert.Empty(cut.FindAll("#account-user-id"));
        Assert.NotNull(cut.Find("#account-heading"));
    }

    [Fact]
    public void AccountPage_RetryAfterFailure_LoadsTheProfile()
    {
        Accessor.GetAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(ApiResponse<CurrentUserDto>.Failed()),
            Task.FromResult(ApiResponse<CurrentUserDto>.Success(UserDto(DisplayName, isAdmin: false))));
        var cut = Render<Account>();

        cut.Find("#account-retry").Click();

        Assert.Equal(DisplayName, cut.Find("#account-display-name").TextContent);
    }

    [Fact]
    [Trait("Category", "Security")]
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
