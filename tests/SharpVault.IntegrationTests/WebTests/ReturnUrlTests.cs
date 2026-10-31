// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class ReturnUrlTests(WebTestFactory factory)
{
    private static readonly Uri Origin = new("https://localhost");

    public static TheoryData<string> UnsafeReturnUrls() =>
    [
        "//evil.example",
        "///evil.example",
        "//evil.example/path",
        "/\\evil.example",
        "/\\/evil.example",
        "\\\\evil.example",
        "\\/evil.example",
        "%2F%2Fevil.example",
        "%2f%2fevil.example",
        "%5C%5Cevil.example",
        "https://evil.example",
        "HTTPS://EVIL.EXAMPLE",
        "http://evil.example",
        "ftp://evil.example",
        "javascript:alert(1)",
        "JaVaScRiPt:alert(1)",
        "data:text/html,<script>alert(1)</script>",
        "evil.example",
        "@evil.example",
        "account",
        " //evil.example",
        " /account",
        "/\t/evil.example",
        "/\n/evil.example",
        "/\r\nSet-Cookie: x=1",
        "/a\u0000b",
        "/a\u007fb",
        "/account\\..\\evil",
        "/" + new string('a', 2049),
        "/" + new string('a', 100_000),
    ];

    public static TheoryData<string> LocalReturnUrls() =>
    [
        "/",
        "/account",
        "/account?x=1",
        "/account#fragment",
        "/%5Cevil.example",
        "/%2F%2Fevil.example",
        "/%2F/evil.example",
        "/.//evil.example",
        "/..//evil.example",
        "/account?next=//evil.example",
        "/account?next=https://evil.example",
    ];

    [Theory]
    [MemberData(nameof(UnsafeReturnUrls))]
    public async Task SignIn_UnsafeReturnUrl_StoresHomeAsRedirect(string returnUrl)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(WebTestSupport.SignInUrl(returnUrl), cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        Assert.Equal("/", WebTestSupport.UnprotectedRedirectUri(factory, response.Headers.Location));
    }

    [Theory]
    [MemberData(nameof(LocalReturnUrls))]
    public async Task SignIn_LocalReturnUrl_StoresOnlyASameOriginPath(string returnUrl)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(WebTestSupport.SignInUrl(returnUrl), cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        var stored = WebTestSupport.UnprotectedRedirectUri(factory, response.Headers.Location);
        Assert.NotNull(stored);
        AssertSameOrigin(stored);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/account")]
    [InlineData("/account?x=1")]
    public async Task SignIn_LocalReturnUrl_StoresItAsRedirect(string returnUrl)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(WebTestSupport.SignInUrl(returnUrl), cancellationToken);

        Assert.Equal(returnUrl, WebTestSupport.UnprotectedRedirectUri(factory, response.Headers.Location));
    }

    [Fact]
    public async Task SignIn_NoReturnUrl_StoresHomeAsRedirect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account/sign-in", cancellationToken);

        Assert.Equal("/", WebTestSupport.UnprotectedRedirectUri(factory, response.Headers.Location));
    }

    [Fact]
    public async Task SignIn_EmptyReturnUrl_StoresHomeAsRedirect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account/sign-in?returnUrl=", cancellationToken);

        Assert.Equal("/", WebTestSupport.UnprotectedRedirectUri(factory, response.Headers.Location));
    }

    [Fact]
    public async Task SignIn_RepeatedReturnUrlParameter_StoresOnlyASameOriginPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account/sign-in?returnUrl=/account&returnUrl=//evil.example", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        var stored = WebTestSupport.UnprotectedRedirectUri(factory, response.Headers.Location);
        Assert.NotNull(stored);
        AssertSameOrigin(stored);
    }

    [Fact]
    public async Task SignIn_ReturnUrlParameterCasingVariant_StoresOnlyASameOriginPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account/sign-in?RETURNURL=%2F%2Fevil.example", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        var stored = WebTestSupport.UnprotectedRedirectUri(factory, response.Headers.Location);
        AssertSameOrigin(stored ?? "/");
    }

    [Theory]
    [MemberData(nameof(UnsafeReturnUrls))]
    public async Task SignIn_UnsafeReturnUrl_NeverAppearsInTheAuthorizeRedirect(string returnUrl)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(WebTestSupport.SignInUrl(returnUrl), cancellationToken);

        var location = response.Headers.Location!.ToString();
        Assert.DoesNotContain("evil.example", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", Uri.UnescapeDataString(location), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://localhost/signin-oidc", WebTestSupport.QueryValue(response.Headers.Location, "redirect_uri"));
    }

    private static void AssertSameOrigin(string redirect)
    {
        Assert.StartsWith("/", redirect, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', redirect);
        Assert.False(redirect.StartsWith("//", StringComparison.Ordinal));
        var normalized = redirect.Replace('\\', '/');
        var resolved = new Uri(Origin, normalized);
        Assert.Equal(Origin.Host, resolved.Host);
        Assert.Equal(Origin.Scheme, resolved.Scheme);
        Assert.Equal(Origin.Port, resolved.Port);
    }
}
