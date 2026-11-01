// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
public sealed partial class ThemeWebTests(WebTestFactory factory)
{
    private const int MaxStylesheets = 2;
    private const int MaxScripts = 2;
    private const int MaxHtmlBytes = 32768;

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScriptPattern();

    [GeneratedRegex(@"<[^>]+\sstyle\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineStylePattern();

    [GeneratedRegex(@"<style\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleElementPattern();

    [GeneratedRegex(@"<[^>]+\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineEventHandlerPattern();

    [GeneratedRegex(@"\b(?:src|href|action|formaction|poster)\s*=\s*[""']\s*(?:https?:)?//", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExternalOriginPattern();

    [GeneratedRegex(@"<link\b[^>]*\brel\s*=\s*[""']?(?:preconnect|dns-prefetch|prefetch|preload|modulepreload|prerender)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResourceHintPattern();

    [GeneratedRegex(@"<meta\b[^>]*\bname=""viewport""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ViewportMetaPattern();

    [GeneratedRegex(@"<meta\b[^>]*\bname=""color-scheme""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorSchemeMetaPattern();

    [GeneratedRegex(@"<meta\b[^>]*\bname=""theme-color""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThemeColorMetaPattern();

    [GeneratedRegex(@"<link\b[^>]*\brel=""icon""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IconLinkPattern();

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
    public async Task ThemedPages_Rendered_ContainNoInlineScriptStyleOrHandlers(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Empty(InlineScriptPattern().Matches(html));
        Assert.Empty(InlineStylePattern().Matches(html));
        Assert.Empty(StyleElementPattern().Matches(html));
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
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Rendered_DeclareViewportColourSchemeAndIcon(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var viewport = Assert.Single(ViewportMetaPattern().Matches(html)).Value;
        Assert.Contains("viewport-fit=cover", viewport, StringComparison.Ordinal);
        var colourScheme = Assert.Single(ColorSchemeMetaPattern().Matches(html)).Value;
        Assert.Contains("content=\"dark light\"", colourScheme, StringComparison.Ordinal);
        var themeColours = ThemeColorMetaPattern().Matches(html);
        Assert.Equal(2, themeColours.Count);
        Assert.Contains(themeColours, match => match.Value.Contains("prefers-color-scheme: dark", StringComparison.Ordinal));
        Assert.Contains(themeColours, match => match.Value.Contains("prefers-color-scheme: light", StringComparison.Ordinal));
        var icon = WebUtility.HtmlDecode(Assert.Single(IconLinkPattern().Matches(html)).Value);
        Assert.Contains("type=\"image/svg+xml\"", icon, StringComparison.Ordinal);
        Assert.StartsWith("_content/SharpVault.UI.Shared/brand/favicon", HrefPattern().Match(icon).Groups["href"].Value, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Rendered_LinkThemeStylesheet(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var themeLinks = StylesheetLinkPattern().Matches(html)
            .Select(match => HrefPattern().Match(match.Value).Groups["href"].Value)
            .Where(href => href.StartsWith("_content/SharpVault.UI.Shared/css/app", StringComparison.Ordinal))
            .ToList();
        Assert.Single(themeLinks);
    }

    [Theory]
    [MemberData(nameof(ThemedRoutes))]
    public async Task ThemedPages_Rendered_StylesheetAndScriptCountsWithinBudget(string route, int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await CreateClientForAsync(status, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var html = Encoding.UTF8.GetString(bytes);
        var stylesheets = StylesheetLinkPattern().Matches(html).Select(match => match.Value).ToList();
        var scripts = ScriptElementPattern().Matches(html).Select(match => match.Value).ToList();
        Assert.True(stylesheets.Count <= MaxStylesheets, $"{stylesheets.Count} stylesheets: {string.Join(" | ", stylesheets)}");
        Assert.True(scripts.Count <= MaxScripts, $"{scripts.Count} scripts: {string.Join(" | ", scripts)}");
        Assert.All(scripts, script => Assert.Contains(" src=", script, StringComparison.Ordinal));
        Assert.True(bytes.Length <= MaxHtmlBytes, $"HTML is {bytes.Length} bytes, budget {MaxHtmlBytes}.");
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

    [Fact]
    public async Task ThemedPages_LinkedStylesheetAndIcon_AreServedAnonymously()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var html = await client.GetStringAsync("/", cancellationToken);
        var stylesheet = StylesheetLinkPattern().Matches(html)
            .Select(match => HrefPattern().Match(match.Value).Groups["href"].Value)
            .Single(href => href.StartsWith("_content/SharpVault.UI.Shared/css/app", StringComparison.Ordinal));
        var icon = HrefPattern().Match(Assert.Single(IconLinkPattern().Matches(html)).Value).Groups["href"].Value;

        var stylesheetResponse = await client.GetAsync("/" + stylesheet, cancellationToken);
        var iconResponse = await client.GetAsync("/" + icon, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, stylesheetResponse.StatusCode);
        Assert.StartsWith("text/css", stylesheetResponse.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, iconResponse.StatusCode);
        Assert.Equal("image/svg+xml", iconResponse.Content.Headers.ContentType?.MediaType);
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

    [Theory]
    [MemberData(nameof(ThemeAssets))]
    public async Task ThemeAssets_AnonymousGet_AreCacheable(string path, string contentType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Headers.ETag);
        var cacheControl = response.HeaderValue(SecurityHeaderNames.CacheControl);
        Assert.False(string.IsNullOrWhiteSpace(cacheControl));
        Assert.DoesNotContain("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ThemeAssets))]
    public async Task ThemeAssets_ConditionalGetWithEtag_Returns304(string path, string contentType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var first = await client.GetAsync(path, cancellationToken);
        Assert.Equal(contentType, first.Content.Headers.ContentType?.MediaType);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.Add(etag);

        var second = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(SecurityHeaderValues.NoSniff, second.HeaderValue(SecurityHeaderNames.XContentTypeOptions));
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
    [InlineData("/_content/SharpVault.UI.Shared/fonts/archivo-latin-wdth-normal.woff2", "PUT")]
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
    public async Task ThemeFont_AnonymousGet_MatchesPinnedBytes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(ThemeAssetPaths.FirstViewFont, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.Equal(90104, bytes.Length);
        Assert.Equal(ThemeAssetPaths.FirstViewFontSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal("wOF2", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Favicon_AnonymousGet_IsServedAndPageNoLongerRequestsFaviconIco()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var favicon = await client.GetAsync(ThemeAssetPaths.Favicon, cancellationToken);
        var page = await client.GetStringAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, favicon.StatusCode);
        Assert.Equal("image/svg+xml", favicon.Content.Headers.ContentType?.MediaType);
        var body = await favicon.Content.ReadAsStringAsync(cancellationToken);
        Assert.StartsWith("<svg", body.TrimStart(), StringComparison.Ordinal);
        Assert.Matches("<link[^>]*rel=\"icon\"[^>]*type=\"image/svg(?:\\+|&#x2B;)xml\"", page);
        Assert.DoesNotContain("favicon.ico", page, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(4, urls.Count);
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
        foreach (var font in ThemeAssetPaths.Fonts)
        {
            data.Add(font, "font/woff2");
        }

        return data;
    }

    private async Task<HttpClient> CreateClientForAsync(int status, CancellationToken cancellationToken) =>
        status == (int)HttpStatusCode.NotFound
            ? await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken)
            : factory.CreateWebClient();
}
