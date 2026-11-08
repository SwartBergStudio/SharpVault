// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
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
    [Trait("Category", "Security")]
    public void SignInErrorPage_Rendered_ShowsNoExceptionDetails()
    {
        var cut = Render<SignInError>();

        Assert.DoesNotContain("Exception", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AADSTS", cut.Markup, StringComparison.Ordinal);
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
}
