// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SignInLinkTests : MudTestContext
{
    [Fact]
    public void Link_NoReturnUrl_HrefEqualsAccountRoutesSignIn()
    {
        var cut = Render<SignInLink>(parameters => parameters.Add(link => link.Id, "sign-in-link"));

        var anchor = cut.Find("a#sign-in-link");
        Assert.Equal(AccountRoutes.SignIn, anchor.GetAttribute("href"));
        Assert.Contains("Sign in", anchor.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/account", "%2Faccount")]
    [InlineData("/admin/users?x=1&y=2", "%2Fadmin%2Fusers%3Fx%3D1%26y%3D2")]
    [InlineData("/a b", "%2Fa%20b")]
    [InlineData("//evil.example", "%2F%2Fevil.example")]
    public void Link_ReturnUrl_IsEscaped(string returnUrl, string escaped)
    {
        var cut = Render<SignInLink>(parameters => parameters
            .Add(link => link.Id, "sign-in-link")
            .Add(link => link.ReturnUrl, returnUrl));

        Assert.Equal($"{AccountRoutes.SignIn}?{AccountRoutes.ReturnUrlParameter}={escaped}", cut.Find("a#sign-in-link").GetAttribute("href"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Link_BlankReturnUrl_IsOmitted(string? returnUrl)
    {
        var cut = Render<SignInLink>(parameters => parameters
            .Add(link => link.Id, "sign-in-link")
            .Add(link => link.ReturnUrl, returnUrl));

        Assert.Equal(AccountRoutes.SignIn, cut.Find("a#sign-in-link").GetAttribute("href"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    public void Link_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SignInLink>(parameters => parameters.Add(link => link.Id, id)));
    }
}
