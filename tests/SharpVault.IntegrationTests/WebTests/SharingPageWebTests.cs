// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingPageWebTests(WebTestFactory factory)
{
    private const string BlazorMarker = "<!--Blazor:";

    public static TheoryData<string> SharingRoutes() => [.. AllRoutes()];

    public static TheoryData<string> MalformedRoutes() =>
    [
        "/shared/not-a-guid/secrets/" + Guid.NewGuid().ToString("D"),
        "/shared/" + Guid.NewGuid().ToString("D") + "/secrets/not-a-guid",
        "/shared/" + Guid.NewGuid().ToString("D") + "/secrets/not-a-guid/edit",
        "/shared/not-a-guid/folders/" + Guid.NewGuid().ToString("D"),
        "/shared/" + Guid.NewGuid().ToString("D") + "/folders/not-a-guid",
    ];

    [Fact]
    public async Task SharingPages_InitialHtml_HasNoPrerenderedDataAndMakesNoApiCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var api = new CountingApiHandler();
        using var host = HostWith(api);
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        foreach (var route in AllRoutes())
        {
            var response = await client.GetAsync(route, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains(BlazorMarker, html, StringComparison.Ordinal);
            Assert.DoesNotContain("prerenderId", html, StringComparison.Ordinal);
            Assert.DoesNotContain("shared-item-link", html, StringComparison.Ordinal);
            Assert.DoesNotContain("shared-secret-heading", html, StringComparison.Ordinal);
            Assert.DoesNotContain("shared-folder-heading", html, StringComparison.Ordinal);
            Assert.DoesNotContain("share-panel", html, StringComparison.Ordinal);
            Assert.Equal(0, api.Calls);
        }
    }

    [Theory]
    [MemberData(nameof(SharingRoutes))]
    public async Task SharingPages_Anonymous_RedirectToSignInAndMakeNoApiCall(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var api = new CountingApiHandler();
        using var host = HostWith(api);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync(route, cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task SharingPages_ShellHasSecurityHeadersAndNoStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = HostWith(new CountingApiHandler());
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        foreach (var route in AllRoutes())
        {
            var response = await client.GetAsync(route, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SecurityHeaderAssertions.AssertWeb(response);
            Assert.Contains("no-store", response.HeaderValue("Cache-Control"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Csp_OnSharingPages_MatchesTheConfiguredPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = HostWith(new CountingApiHandler());
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        foreach (var route in AllRoutes())
        {
            var response = await client.GetAsync(route, cancellationToken);

            SecurityHeaderAssertions.AssertWeb(response);
            var csp = response.HeaderValue("Content-Security-Policy");
            Assert.Contains("style-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
            Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
            Assert.DoesNotContain("'unsafe-eval'", csp, StringComparison.Ordinal);
            Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SharingPages_NoInlineScriptOrHandler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = HostWith(new CountingApiHandler());
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        foreach (var route in AllRoutes())
        {
            var html = await client.GetStringAsync(route, cancellationToken);

            Assert.False(WebTestSupport.HasInlineScriptOrHandler(html), route);
            Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(MalformedRoutes))]
    public async Task SharingPages_MalformedGuid_ServesNoSharingPageAndMakesNoApiCall(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var api = new CountingApiHandler();
        using var host = HostWith(api);
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync(route, cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("shared-secret-heading", html, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-folder-heading", html, StringComparison.Ordinal);
        Assert.Equal(0, api.Calls);
    }

    private static string[] AllRoutes()
    {
        var owner = Guid.NewGuid().ToString("D");
        var item = Guid.NewGuid().ToString("D");
        return
        [
            "/shared",
            $"/shared/{owner}/secrets/{item}",
            $"/shared/{owner}/secrets/{item}/edit",
            $"/shared/{owner}/folders/{item}",
        ];
    }

    private WebApplicationFactory<WebApp::Program> HostWith(CountingApiHandler api) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.UsePrimaryHandler(() => api)));
}
