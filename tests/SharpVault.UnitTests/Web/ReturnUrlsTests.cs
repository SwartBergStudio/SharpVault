// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

/// <summary>
/// W3: the sign-in return URL can only ever point back at this site.
/// </summary>
[Trait("Category", "Security")]
public sealed class ReturnUrlsTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/account")]
    [InlineData("/account?x=1")]
    [InlineData("/account#a")]
    [InlineData("/a/b/c")]
    [InlineData("/account?next=%2F%2Fevil.example")]
    [InlineData("/%2F%2Fevil.example")]
    [InlineData("/%5Cevil.example")]
    [InlineData("/account/access-denied")]
    public void Sanitize_LocalPath_IsKept(string returnUrl)
    {
        Assert.Equal(returnUrl, ReturnUrls.Sanitize(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://evil.example")]
    [InlineData("HTTPS://EVIL.EXAMPLE")]
    [InlineData("http:/evil.example")]
    [InlineData("//evil.example")]
    [InlineData("///evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("\\/evil.example")]
    [InlineData("/a\\b")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("account")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData(" /account")]
    [InlineData("/a\u0000b")]
    [InlineData("/a\r\nLocation: x")]
    [InlineData("/\t/evil.example")]
    [InlineData("/\n/evil.example")]
    [InlineData("/a\u0085b")]
    [InlineData("/a\u007fb")]
    public void Sanitize_UnsafeValue_ReturnsRoot(string? returnUrl)
    {
        Assert.Equal("/", ReturnUrls.Sanitize(returnUrl));
    }

    [Fact]
    public void Sanitize_AtMaximumLength_IsKept()
    {
        var value = "/" + new string('a', ReturnUrls.MaxLength - 1);

        Assert.Equal(value, ReturnUrls.Sanitize(value));
    }

    [Fact]
    public void Sanitize_OneOverMaximumLength_ReturnsRoot()
    {
        var value = "/" + new string('a', ReturnUrls.MaxLength);

        Assert.Equal("/", ReturnUrls.Sanitize(value));
    }

    [Fact]
    public void Sanitize_VeryLongValue_ReturnsRoot()
    {
        Assert.Equal("/", ReturnUrls.Sanitize("/" + new string('a', 1_000_000)));
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/")]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    public void Sanitize_AnyInput_ResultIsIdempotentAndLocal(string returnUrl)
    {
        var once = ReturnUrls.Sanitize(returnUrl);

        Assert.Equal(once, ReturnUrls.Sanitize(once));
        Assert.StartsWith("/", once, StringComparison.Ordinal);
        Assert.False(once.StartsWith("//", StringComparison.Ordinal));
    }
}
