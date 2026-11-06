// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class ShellTests(WebTestFactory factory)
{
    private const string BlazorMarker = "<!--Blazor:";
    private const string ServerMarker = "\"type\":\"server\"";
    private const string IdleMonitorPath = "/_content/SharpVault.UI.Shared/js/idle-monitor.js";
    private const string SignedInStaticPage = "/account/access-denied";

    public static TheoryData<string> AnonymousPages() =>
        ["/", "/account/signed-out", "/account/access-denied", "/account/sign-in-error", "/not-found", "/error"];

    [Fact]
    public async Task Home_Anonymous_Returns200StaticHtmlWithSignInLinkAndNoBlazorServerMarker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"home-sign-in\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/account/sign-in\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(BlazorMarker, html, StringComparison.Ordinal);
        Assert.DoesNotContain(ServerMarker, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_SignedIn_RedirectsToTheVaultWithoutRenderingTheUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/vault", response.Headers.Location?.AbsolutePath);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(TestUsers.User.DisplayName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestUsers.User.Email!, body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"home-greeting\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vault_SignedIn_ContainsNoInlineScript()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync("/vault", cancellationToken);

        Assert.False(WebTestSupport.HasInlineScriptOrHandler(html));
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Account_Anonymous_RedirectsToSignIn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Fact]
    public async Task Account_Anonymous_NeverCallsTheApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stub = new CountingApiHandler();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.UsePrimaryHandler(() => stub)));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync("/account", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task Account_SignedIn_ShellHasSecurityHeadersAndNoStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SecurityHeaderAssertions.AssertWeb(response);
        Assert.Contains("no-store", response.HeaderValue("Cache-Control"), StringComparison.Ordinal);
        Assert.False(WebTestSupport.HasInlineScriptOrHandler(await response.Content.ReadAsStringAsync(cancellationToken)));
    }

    [Theory]
    [MemberData(nameof(AnonymousPages))]
    public async Task Csp_OnEveryPage_MatchesTheConfiguredPolicy(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(route, cancellationToken);

        SecurityHeaderAssertions.AssertWeb(response);
        var csp = response.HeaderValue("Content-Security-Policy");
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csp_OnSignedInPages_MatchesTheConfiguredPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        foreach (var route in new[] { "/account", "/admin/users", "/vault", SignedInStaticPage })
        {
            var response = await client.GetAsync(route, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SecurityHeaderAssertions.AssertWeb(response);
        }
    }

    [Fact]
    public async Task SignedInStaticPage_ContainsTheSignOutFormWithAnAntiforgeryToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync(SignedInStaticPage, cancellationToken);

        Assert.False(string.IsNullOrEmpty(WebTestSupport.ExtractSignOutAntiforgeryToken(html)));
        Assert.Contains("id=\"account-name\"", html, StringComparison.Ordinal);
        Assert.Contains(TestUsers.User.DisplayName, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedInStaticPage_LoadsTheIdleBootScriptWithTheSignOutFormAndTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync(SignedInStaticPage, cancellationToken);

        Assert.Contains("id=\"idle-boot\"", html, StringComparison.Ordinal);
        Assert.Contains("data-idle-form=\"sign-out-form\"", html, StringComparison.Ordinal);
        Assert.Contains("data-idle-timeout-ms=\"900000\"", html, StringComparison.Ordinal);
        Assert.Contains("js/idle-boot", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AnonymousPages))]
    public async Task AnonymousPages_NeverLoadTheIdleBootScript(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var html = await client.GetStringAsync(route, cancellationToken);

        Assert.DoesNotContain("idle-boot", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AnonymousPages))]
    public async Task AnonymousPages_LoadTheClipboardBootScriptSoAPendingClearSurvivesSignOut(string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var html = await client.GetStringAsync(route, cancellationToken);

        Assert.Contains("id=\"clipboard-boot\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedInInteractivePage_DoesNotLoadTheStaticIdleBootScript()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync("/vault", cancellationToken);

        Assert.DoesNotContain("idle-boot", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminUsersRoute_SignedInUser_ReturnsTheShellOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/admin/users", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("id=\"admin-users-table\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminUsersRoute_Anonymous_IsChallenged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/admin/users", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Fact]
    public async Task Shell_SignedIn_HtmlContainsNoTokenOrSegmentOfIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);

        foreach (var route in new[] { "/", "/account", "/admin/users", "/vault", SignedInStaticPage })
        {
            var response = await client.GetAsync(route, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.False(WebTestSupport.ContainsJwt(html), "The page contains a JWT-shaped string.");
            Assert.DoesNotContain("Bearer ", html, StringComparison.Ordinal);
            foreach (var segment in token.Split('.'))
            {
                Assert.DoesNotContain(segment, html, StringComparison.Ordinal);
            }

            Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.Contains(token[..30], StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Shell_SignedIn_RequestsLogNoTokensNamesOrEmails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        factory.Logs.Clear();

        foreach (var route in new[] { "/", "/account", SignedInStaticPage })
        {
            await client.GetAsync(route, cancellationToken);
        }

        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);
        Assert.False(factory.Logs.ContainsText(token.Split('.')[1]), "The token payload was logged.");
        Assert.False(factory.Logs.ContainsText(TestUsers.User.DisplayName), "The display name was logged.");
        Assert.False(factory.Logs.ContainsText(TestUsers.User.Email!), "The email address was logged.");
        Assert.False(factory.Logs.ContainsText("Bearer eyJ"), "An authorization header value was logged.");
    }

    [Fact]
    public async Task StaticAssets_IdleMonitorModule_IsServed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(IdleMonitorPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        var script = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("export function start", script, StringComparison.Ordinal);
        Assert.Contains("export function stop", script, StringComparison.Ordinal);
        Assert.Contains("requestSubmit", script, StringComparison.Ordinal);
        Assert.Contains("localStorage", script, StringComparison.Ordinal);
        Assert.Contains("invokeMethodAsync(\"Heartbeat\")", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"invokeMethodAsync\((?!\s*""Heartbeat"")", script);
        Assert.DoesNotContain("fetch(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.Ordinal);
    }
}
