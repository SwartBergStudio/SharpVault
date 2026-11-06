// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Layout;

public sealed class AccountMenuTests : ShellTestContext
{
    [Fact]
    public void AccountMenu_SignedIn_ShowsNameLinkAndPostForm()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = Render<AccountMenu>();

        Assert.Equal(DisplayName, cut.Find("#account-name").TextContent);
        Assert.Equal(0, cut.Find("#account-name").ChildElementCount);
        Assert.Contains("Account", cut.Find("#account-link").TextContent, StringComparison.Ordinal);
        var form = cut.Find("form#sign-out-form");
        Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
        var button = form.QuerySelector("button#sign-out-button");
        Assert.NotNull(button);
        Assert.Contains("Sign out", button.TextContent, StringComparison.Ordinal);
        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Empty(cut.FindAll("#sign-in-link"));
    }

    [Theory]
    [InlineData("account/local-sign-in?returnUrl=%2F")]
    [InlineData("account/sign-in")]
    public void AccountMenu_SignedOutOnASignInPage_HidesTheRedundantSignInButton(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/" + path);

        var cut = Render<AccountMenu>();

        Assert.Empty(cut.FindAll("#sign-in-link"));
    }

    [Fact]
    public void AccountMenu_SignedOutOnAnyOtherPage_ShowsTheSignInButton()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/account/signed-out");

        var cut = Render<AccountMenu>();

        Assert.NotNull(cut.Find("#sign-in-link"));
    }

    [Fact]
    public void AccountMenu_AccountLink_UsesAccountRoutesAccount()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = Render<AccountMenu>();

        Assert.Equal(AccountRoutes.Account, cut.Find("a#account-link").GetAttribute("href"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountMenu_DisplayNameWithMarkup_IsEncoded()
    {
        const string hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        Authorization.SetAuthorized(hostile);

        var cut = Render<AccountMenu>();

        Assert.Equal(hostile, cut.Find("#account-name").TextContent);
        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("<img", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountMenu_SignOutForm_PostsToSignOutRouteWithAntiforgeryOnly()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = Render<AccountMenu>();

        var form = cut.Find("#sign-out-form");
        Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
        Assert.Equal(AccountRoutes.SignOut, form.GetAttribute("action"));
        Assert.DoesNotContain(cut.FindAll("a"), anchor => anchor.GetAttribute("href")?.Contains("sign-out", StringComparison.Ordinal) == true);
        Assert.Empty(form.QuerySelectorAll("input[type=text]"));
        Assert.Equal("sign-out-form", form.Id);
    }

    [Fact]
    public void AccountMenu_SignedOut_ShowsSignInLinkOnly()
    {
        var cut = Render<AccountMenu>();

        var link = cut.Find("a#sign-in-link");
        Assert.Equal(AccountRoutes.SignIn, link.GetAttribute("href"));
        Assert.Contains("Sign in", link.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#sign-out-form"));
        Assert.Empty(cut.FindAll("#account-name"));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountMenu_SignedOut_ShowsNoUserData()
    {
        var cut = Render<AccountMenu>();

        Assert.DoesNotContain(DisplayName, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHeader_Rendered_BrandLinkHasTextAndHomeRoute()
    {
        var cut = Render<AppHeader>();

        var brand = cut.Find("header#app-header a#brand-home-link");
        Assert.Contains("SharpVault", brand.TextContent, StringComparison.Ordinal);
        Assert.Equal(AccountRoutes.Home, brand.GetAttribute("href"));
        Assert.NotNull(cut.Find("header#app-header nav#account-menu"));
    }

    [Fact]
    public void AppHeader_MenuToggleHidden_RendersNoToggle()
    {
        var cut = Render<AppHeader>();

        Assert.Empty(cut.FindAll("#nav-toggle"));
    }

    [Fact]
    public void AppHeader_MenuToggleShown_RaisesTheCallbackAndIsLabelled()
    {
        var clicks = 0;
        var cut = Render<AppHeader>(parameters => parameters
            .Add(header => header.ShowMenuToggle, true)
            .Add(header => header.OnMenuToggle, () => clicks++));

        var toggle = cut.Find("#nav-toggle");
        Assert.Equal("Toggle navigation", toggle.GetAttribute("aria-label"));
        toggle.Click();

        Assert.Equal(1, clicks);
    }
}
