// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.RegularExpressions;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
public sealed partial class ThemeWebTests(WebTestFactory factory)
{
    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScriptPattern();

    [GeneratedRegex(@"<[^>]+\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineEventHandlerPattern();

    [GeneratedRegex(@"\b(?:src|href|action|formaction|poster)\s*=\s*[""']\s*(?:https?:)?//", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExternalOriginPattern();

    [GeneratedRegex(@"<link\b[^>]*\brel\s*=\s*[""']?(?:preconnect|dns-prefetch|prefetch|preload|modulepreload|prerender)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResourceHintPattern();

    [GeneratedRegex(@"<link\b[^>]*\brel=""stylesheet""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StylesheetLinkPattern();

    [GeneratedRegex(@"<script\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptElementPattern();

    [GeneratedRegex(@"\bhref=""(?<href>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefPattern();

    [GeneratedRegex(@"\bsrc=""(?<src>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SrcPattern();

    public static TheoryData<string, int> ThemedRoutes { get; } = new()
    {
        { "/", 200 },
        { "/does-not-exist", 404 },
        { "/account/access-denied", 200 },
        { "/account/signed-out", 200 },
        { "/account/sign-in-error", 200 },
    };

    public static TheoryData<string> AnonymousRoutes { get; } = new()
    {
        "/",
        "/account/access-denied",
        "/account/signed-out",
        "/account/sign-in-error",
    };

    public static TheoryData<string, string> ThemeAssets { get; } = BuildAssetData();

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Response_HaveWebSecurityHeaders(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Rendered_ContainNoInlineScriptOrHandlers(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Empty(InlineScriptPattern().Matches(html));
        Assert.Empty(InlineEventHandlerPattern().Matches(html));
        Assert.DoesNotContain("importmap", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Rendered_LoadNoExternalOrigins(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var external = ExternalOriginPattern().Matches(html).Select(match => match.Value).ToList();
        Assert.True(external.Count == 0, $"External origins found: {string.Join(" | ", external)}");
        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jsdelivr", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdnjs", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unpkg.com", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Rendered_HaveNoResourceHints(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var hints = ResourceHintPattern().Matches(html).Select(match => match.Value).ToList();
        Assert.True(hints.Count == 0, $"Resource hints found: {string.Join(" | ", hints)}");
        Assert.DoesNotContain("rel=\"preconnect\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"dns-prefetch\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"prefetch\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"preload\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Response_SendNoResourceHintLinkHeader(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var link = response.HeaderValue("Link");
        Assert.True(
            link is null || !Regex.IsMatch(link, "rel=\"?(preconnect|dns-prefetch|prefetch|preload|modulepreload)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            $"Link header carries resource hints: {link}");
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Rendered_ReferenceOnlySameOriginStylesheetsAndScripts(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var stylesheetHrefs = StylesheetLinkPattern().Matches(html).Select(match => HrefPattern().Match(match.Value).Groups["href"].Value);
        var scriptSources = ScriptElementPattern().Matches(html).Select(match => SrcPattern().Match(match.Value).Groups["src"].Value);
        Assert.All(stylesheetHrefs.Concat(scriptSources), url =>
        {
            Assert.False(string.IsNullOrEmpty(url));
            Assert.DoesNotContain("://", url, StringComparison.Ordinal);
            Assert.False(url.StartsWith("//", StringComparison.Ordinal), $"Protocol-relative URL {url}");
        });
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemeAssets))]
    public async Task ThemeAssets_AnonymousGet_ServeWithNoSniffAndSameOriginPolicy(string path, string contentType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(WebTestSupport.SetCookies(response));
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SecurityHeaderValues.NoSniff, response.HeaderValue(SecurityHeaderNames.XContentTypeOptions));
        Assert.Equal(SecurityHeaderValues.SameOrigin, response.HeaderValue(SecurityHeaderNames.CrossOriginResourcePolicy));
        Assert.Equal(SecurityHeaderValues.FrameDeny, response.HeaderValue(SecurityHeaderNames.XFrameOptions));
        var cacheControl = response.HeaderValue(SecurityHeaderNames.CacheControl);
        Assert.False(cacheControl?.Contains("no-store", StringComparison.OrdinalIgnoreCase) == true, $"Cache-Control: {cacheControl}");
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ThemeAssets))]
    public async Task ThemeAssets_AnonymousGet_AreNotRedirectedToSignIn(string path, string contentType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ThemeAssets_Response_HasWebContentSecurityPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(ThemeAssetPaths.Stylesheet, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/_content/SharpVault.UI.Shared/css/app.css", "POST")]
    [InlineData("/_content/SharpVault.UI.Shared/js/clipboard.js", "PUT")]
    [InlineData("/_content/SharpVault.UI.Shared/brand/favicon.svg", "DELETE")]
    public async Task ThemeAssets_WriteMethods_AreNotAccepted(string path, string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.SendAsync(method, path, cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/_content/SharpVault.UI.Shared/../../appsettings.json")]
    [InlineData("/_content/SharpVault.UI.Shared/fonts/..%2f..%2fcss%2fapp.css.map")]
    [InlineData("/_content/SharpVault.UI.Shared/%2e%2e/%2e%2e/appsettings.json")]
    [InlineData("/_content/SharpVault.UI.Shared/Components/Icons/Icon.razor")]
    [InlineData("/_content/SharpVault.UI.Shared/SharpVault.UI.Shared.csproj")]
    public async Task ThemeAssets_PathTraversalOrSourceFiles_AreNotServed(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("AzureAd", body, StringComparison.Ordinal);
        Assert.DoesNotContain("@code", body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Favicon_AnonymousGet_ContainsNoScriptStyleOrExternalReferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var body = await client.GetStringAsync(ThemeAssetPaths.Favicon, cancellationToken);

        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreignObject", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", body.Replace("xmlns=\"http://www.w3.org/2000/svg\"", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ThemeStylesheet_AnonymousGet_ReferencesOnlyBundledFonts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var css = await client.GetStringAsync(ThemeAssetPaths.Stylesheet, cancellationToken);

        Assert.DoesNotContain("@import", css, StringComparison.Ordinal);
        Assert.DoesNotContain("http:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("https:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("data:", css, StringComparison.Ordinal);
        var urls = Regex.Matches(css, @"url\(([^)]*)\)").Select(match => match.Groups[1].Value).ToList();
        Assert.All(urls, url => Assert.Matches("^\"\\.\\./fonts/[a-z-]+\\.woff2\"$", url));
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(AnonymousRoutes))]
    public async Task ThemedPages_Rendered_ContainNoUserDataForAnonymousVisitors(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(TestUsers.User.DisplayName, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Signed in as", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sign-out-form\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"side-nav\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"skip-to-content\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", html, StringComparison.Ordinal);
    }

    private static TheoryData<string, string> BuildAssetData()
    {
        var data = new TheoryData<string, string>
        {
            { ThemeAssetPaths.Stylesheet, "text/css" },
            { ThemeAssetPaths.Favicon, "image/svg+xml" },
        };
        return data;
    }

    private async Task<HttpClient> CreateClientForAsync(int status, CancellationToken cancellationToken) =>
        status == (int)HttpStatusCode.NotFound
            ? await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken)
            : factory.CreateWebClient();
}
