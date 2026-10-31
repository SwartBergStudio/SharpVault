// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
public sealed partial class WebHostTests(WebTestFactory factory)
{
    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScriptPattern();

    [GeneratedRegex(@"<[^>]+\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineEventHandlerPattern();

    [GeneratedRegex(@"<[^>]+\sstyle\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineStylePattern();

    [Fact]
    public async Task HomePage_AnonymousGet_Returns200WithHeading()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"home-heading\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WebHost_SecretInQueryString_IsNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var secret = "query-secret-" + Guid.NewGuid().ToString("N");
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync($"/does-not-exist?token={secret}", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        Assert.DoesNotContain(secret, location, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, Uri.UnescapeDataString(location), StringComparison.Ordinal);

        Assert.Contains(
            factory.Logs.Entries,
            entry => entry.Category.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) && entry.Level == LogLevel.Debug);
        Assert.DoesNotContain(
            factory.Logs.Entries,
            entry => entry.Category == "Microsoft.AspNetCore.Hosting.Diagnostics" && entry.Level < LogLevel.Warning);
        Assert.False(factory.Logs.ContainsText(secret), "A captured log entry contained the query string secret.");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HomePage_Response_HasWebSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HomePage_Response_DoesNotAdvertiseServerOrFramework()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        Assert.Null(response.HeaderValue("Server"));
        Assert.Null(response.HeaderValue("X-Powered-By"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HomePage_Rendered_ContainsNoInlineScript()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Empty(InlineScriptPattern().Matches(html));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HomePage_Rendered_ContainsNoInlineEventHandlersOrStyles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Empty(InlineEventHandlerPattern().Matches(html));
        Assert.Empty(InlineStylePattern().Matches(html));
        Assert.DoesNotContain("importmap", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HomePage_Rendered_LoadsNoExternalOrigins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("src=\"http", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"http", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=\"//", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HomePage_Response_SendsNoStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        Assert.Contains(SecurityHeaderValues.NoStore, response.HeaderValue(SecurityHeaderNames.CacheControl), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPage_Get_Returns404WithNotFoundPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"not-found-heading\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnknownPage_Get_HasWebSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnknownPage_AnonymousGet_RedirectsToTenantAuthorizeEndpointWithWebSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal("login.microsoftonline.com", location.Host);
        Assert.StartsWith($"/{TestTenant.Id:D}/oauth2/v2.0/authorize", location.AbsolutePath, StringComparison.Ordinal);
        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnhandledException_WebAnonymous_IsChallengedWithoutReachingTheThrowingEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = CreateThrowingFactory();
        using var client = throwing.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(ThrowingRequestStartupFilter.Path, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(SensitiveMarkers.ExceptionMessage, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnhandledException_Web_RendersErrorPageWithoutExceptionDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = CreateThrowingFactory();
        using var client = await WebSignIn.CreateClientAsync(throwing, TestUsers.User, cancellationToken);

        var response = await client.GetAsync(ThrowingRequestStartupFilter.Path, cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"error-heading\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveMarkers.ExceptionMessage, html, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" at SharpVault", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnhandledException_Web_ErrorPageHasWebSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = CreateThrowingFactory();
        using var client = await WebSignIn.CreateClientAsync(throwing, TestUsers.User, cancellationToken);

        var response = await client.GetAsync(ThrowingRequestStartupFilter.Path, cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Fact]
    public async Task WebHealth_AnonymousGet_Returns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task WebAlive_AnonymousGet_Returns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/alive", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task WebHealthEndpoints_PostRequest_IsNotServed(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.PostAsync(path, new StringContent("{}"), cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task WebHost_ApiDocumentationRoutes_AreNotServed(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task WebHost_ApiDocumentationRoutesAnonymous_AreChallengedNotServed(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("login.microsoftonline.com", response.Headers.Location?.Host);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WebHost_CorsPreflightFromUntrustedOrigin_ReturnsNoAllowOriginHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request, cancellationToken);

        Assert.Null(response.HeaderValue("Access-Control-Allow-Origin"));
    }

    private WebApplicationFactory<WebApp::Program> CreateThrowingFactory() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, ThrowingRequestStartupFilter>()));
}
