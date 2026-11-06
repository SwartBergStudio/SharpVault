// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using WebApp::SharpVault.Web.Api;

namespace SharpVault.UnitTests.Web;

public sealed class WebSessionActionsTests
{
    private readonly WebSessionActions _actions = new();

    [Fact]
    public void Properties_Always_PointAtTheAccountEndpoints()
    {
        Assert.Equal("sign-out-form", _actions.SignOutFormId);
        Assert.Equal("/account/sign-out", _actions.SignOutAction);
        Assert.Equal("/account/access-denied", _actions.AccessDeniedUrl);
    }

    [Theory]
    [InlineData(null, "/account/sign-in")]
    [InlineData("", "/account/sign-in")]
    [InlineData("  ", "/account/sign-in")]
    [InlineData("/account", "/account/sign-in?returnUrl=%2Faccount")]
    [InlineData("/a?b=c&d=e", "/account/sign-in?returnUrl=%2Fa%3Fb%3Dc%26d%3De")]
    public void SignInUrl_ReturnUrl_IsEscaped(string? returnUrl, string expected)
    {
        Assert.Equal(expected, _actions.SignInUrl(returnUrl));
    }

    [Theory]
    [InlineData("account/sign-in", true)]
    [InlineData("account/sign-in/", true)]
    [InlineData("Account/Local-Sign-In?returnUrl=%2F", true)]
    [InlineData("account/local-sign-in#top", true)]
    [InlineData("account/signed-out", false)]
    [InlineData("account/sign-in-error", false)]
    [InlineData("", false)]
    [InlineData("admin/users", false)]
    public void IsSignInPage_Path_MatchesOnlyTheSignInPages(string path, bool expected)
    {
        Assert.Equal(expected, _actions.IsSignInPage(path));
    }

    [Fact]
    public void IsSignInPage_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _actions.IsSignInPage(null!));
    }
}
