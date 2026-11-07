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
    [InlineData("/account?next=%2F%2Fevil.example")]
    [InlineData("/%2F%2Fevil.example")]
    [InlineData("/%5Cevil.example")]
    public void Sanitize_LocalPath_IsKept(string returnUrl)
    {
        Assert.Equal(returnUrl, ReturnUrls.Sanitize(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("account")]
    [InlineData(" /account")]
    [InlineData("/a\u0000b")]
    [InlineData("/a\r\nLocation: x")]
    [InlineData("/\t/evil.example")]
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

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    public void Sanitize_UnsafeInput_ResultIsIdempotentAndLocal(string returnUrl)
    {
        var once = ReturnUrls.Sanitize(returnUrl);

        Assert.Equal(once, ReturnUrls.Sanitize(once));
        Assert.StartsWith("/", once, StringComparison.Ordinal);
        Assert.False(once.StartsWith("//", StringComparison.Ordinal));
    }
}
