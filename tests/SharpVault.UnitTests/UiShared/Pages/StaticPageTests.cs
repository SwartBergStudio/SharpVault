// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Pages;

public sealed class StaticPageTests : ShellTestContext
{
    [Theory]
    [InlineData(typeof(AccessDenied), "/account/access-denied")]
    [InlineData(typeof(SignedOut), "/account/signed-out")]
    [InlineData(typeof(SignInError), "/account/sign-in-error")]
    [InlineData(typeof(NotFound), "/not-found")]
    [InlineData(typeof(Error), "/error")]
    public void StaticPages_Declare_TheirRouteAnonymousAccessAndStaticRendering(Type page, string route)
    {
        Assert.Equal(route, page.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotNull(page.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
        Assert.NotNull(page.GetCustomAttribute<ExcludeFromInteractiveRoutingAttribute>());
    }

    [Fact]
    public void AccessDeniedPage_Rendered_IsAnAlertWithHeadingMessageAndHomeLink()
    {
        var cut = Render<AccessDenied>();

        var panel = cut.Find("#access-denied");
        Assert.Equal("alert", panel.GetAttribute("role"));
        Assert.NotNull(cut.Find("#access-denied-heading"));
        Assert.Equal("access-denied-heading", panel.GetAttribute("aria-labelledby"));
        Assert.NotNull(cut.Find("#access-denied-message"));
        var home = cut.Find("a#access-denied-home-link");
        Assert.Equal(AccountRoutes.Home, home.GetAttribute("href"));
    }

    [Fact]
    public void AccessDeniedPage_Anonymous_ShowsTheStartPageLink()
    {
        var cut = Render<AccessDenied>();

        Assert.NotNull(cut.Find("a#access-denied-home-link"));
    }

    [Fact]
    public void AccessDeniedPage_SignedIn_HidesTheStartPageLink()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = Render<AccessDenied>();

        Assert.Empty(cut.FindAll("#access-denied-home-link"));
        Assert.NotNull(cut.Find("#access-denied-heading"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccessDeniedPage_Rendered_ShowsNoUserData()
    {
        Authorization.SetAuthorized(DisplayName);

        var cut = Render<AccessDenied>();

        Assert.DoesNotContain(DisplayName, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedOutPage_Rendered_ShowsHeadingAndSignInLink()
    {
        var cut = Render<SignedOut>();

        Assert.NotNull(cut.Find("#signed-out-heading"));
        var link = cut.Find("a#signed-out-sign-in-link");
        Assert.Equal(AccountRoutes.SignIn, link.GetAttribute("href"));
    }

    [Fact]
    public void SignedOutPage_AfterSignOut_ShowsAnonymousState()
    {
        var cut = Render<SignedOut>();

        Assert.Empty(cut.FindAll("#account-name"));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void SignInErrorPage_Rendered_ShowsHeadingMessageAndRetryLink()
    {
        var cut = Render<SignInError>();

        Assert.NotNull(cut.Find("#sign-in-error-heading"));
        Assert.False(string.IsNullOrWhiteSpace(cut.Find("#sign-in-error-message").TextContent));
        var retry = cut.Find("a#sign-in-error-retry-link");
        Assert.Equal(AccountRoutes.SignIn, retry.GetAttribute("href"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SignInErrorPage_Rendered_ShowsNoExceptionDetails()
    {
        var cut = Render<SignInError>();

        Assert.DoesNotContain("Exception", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AADSTS", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NotFoundPage_Rendered_IsAStatusPanelWithHeadingAndHomeLink()
    {
        var cut = Render<NotFound>();

        var panel = cut.Find("#not-found");
        Assert.Equal("status", panel.GetAttribute("role"));
        Assert.NotNull(cut.Find("#not-found-heading"));
        Assert.NotNull(cut.Find("#not-found-message"));
        Assert.Equal(AccountRoutes.Home, cut.Find("a#not-found-home-link").GetAttribute("href"));
    }

    [Fact]
    public void ErrorPage_Rendered_ShowsHeadingMessageAndHomeLink()
    {
        var cut = Render<Error>();

        Assert.NotNull(cut.Find("#error-heading"));
        Assert.NotNull(cut.Find("#error-message"));
        Assert.Equal(AccountRoutes.Home, cut.Find("a#error-home-link").GetAttribute("href"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ErrorPage_ShowsRequestIdOnlyNotExceptionText()
    {
        using var activity = new System.Diagnostics.Activity("request").Start();

        var cut = Render<Error>();

        var requestId = cut.Find("#error-request-id");
        Assert.Contains(activity.Id!, requestId.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorPage_WithoutActivity_ShowsNoRequestId()
    {
        System.Diagnostics.Activity.Current = null;

        var cut = Render<Error>();

        Assert.Empty(cut.FindAll("#error-request-id"));
    }
}
