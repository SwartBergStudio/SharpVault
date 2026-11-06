// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultPageWebTests(WebTestFactory factory)
{
    private const string BlazorMarker = "<!--Blazor:";
    private const string ClipboardModulePath = "/_content/SharpVault.UI.Shared/js/clipboard.js";
    private const string ComponentHubTypeName = "Microsoft.AspNetCore.Components.Server.ComponentHub";
    private const int ExpectedHubReceiveLimit = 64 * 1024;

    public static TheoryData<string> VaultRoutes() => [.. AllRoutes()];

    [Fact]
    public async Task VaultPages_InitialHtml_ContainNoSecretDataAndNoApiCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var api = new CountingApiHandler();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.UsePrimaryHandler(() => api)));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        foreach (var route in AllRoutes())
        {
            var response = await client.GetAsync(route, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains(BlazorMarker, html, StringComparison.Ordinal);
            Assert.DoesNotContain("prerenderId", html, StringComparison.Ordinal);
            Assert.DoesNotContain("vault-secret-link", html, StringComparison.Ordinal);
            Assert.Equal(0, api.Calls);
        }
    }

    [Theory]
    [MemberData(nameof(VaultRoutes))]
    public async Task VaultPages_Anonymous_RedirectToSignIn(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var api = new CountingApiHandler();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.UsePrimaryHandler(() => api)));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync(route, cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task VaultPages_NoInlineScriptOrHandler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await SignedInStubbedClientAsync(cancellationToken);

        foreach (var route in AllRoutes())
        {
            var html = await client.GetStringAsync(route, cancellationToken);

            Assert.False(WebTestSupport.HasInlineScriptOrHandler(html), route);
            Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task VaultPages_ShellHasSecurityHeadersAndNoStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await SignedInStubbedClientAsync(cancellationToken);

        foreach (var route in AllRoutes())
        {
            var response = await client.GetAsync(route, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SecurityHeaderAssertions.AssertWeb(response);
            Assert.Contains("no-store", response.HeaderValue("Cache-Control"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ClipboardModule_IsServedAsLocalAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(ClipboardModulePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        var script = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("export async function writeText", script, StringComparison.Ordinal);
        Assert.Contains("export async function clear", script, StringComparison.Ordinal);
        Assert.Contains("navigator.clipboard.writeText", script, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WebHost_BlazorHub_ReceiveLimitIs64KiB()
    {
        var componentHub = typeof(Microsoft.AspNetCore.Components.Server.CircuitOptions).Assembly.GetType(ComponentHubTypeName);
        Assert.NotNull(componentHub);
        var optionsType = typeof(IOptions<>).MakeGenericType(typeof(HubOptions<>).MakeGenericType(componentHub));

        var options = (IOptions<HubOptions>)factory.Services.GetRequiredService(optionsType);

        Assert.Equal(ExpectedHubReceiveLimit, options.Value.MaximumReceiveMessageSize);
    }

    private static string[] AllRoutes()
    {
        var id = Guid.NewGuid().ToString("D");
        return
        [
            "/vault",
            "/vault/unfiled",
            "/vault/recycle-bin",
            "/vault/secrets/new",
            $"/vault/secrets/{id}",
            $"/vault/secrets/{id}/edit",
            $"/vault/secrets/{id}/history",
            $"/vault/folders/{id}",
        ];
    }

    private async Task<HttpClient> SignedInStubbedClientAsync(CancellationToken cancellationToken)
    {
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.UsePrimaryHandler(() => new CountingApiHandler())));
        return await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);
    }
}
