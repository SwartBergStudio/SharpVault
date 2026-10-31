// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class WebAuthenticationTests(WebTestFactory factory)
{
    private const string SessionCookieName = "__Host-SharpVault";

    [Fact]
    public async Task AccountPage_Anonymous_RedirectsToTenantAuthorizeEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        var location = response.Headers.Location!.ToString();
        Assert.DoesNotContain("/common/", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/organizations/", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/consumers/", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestTenant.OtherId.ToString("D"), location, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/account/")]
    [InlineData("/ACCOUNT")]
    [InlineData("/does-not-exist")]
    [InlineData("/admin")]
    [InlineData("/_framework/anything")]
    [InlineData("/account/sign-out")]
    [InlineData("/account/unknown")]
    [InlineData("/%2e%2e/account")]
    [InlineData("/account;x=1")]
    [InlineData("/test/throw")]
    public async Task ProtectedOrUnknownPaths_Anonymous_AreChallengedToTenantAuthorizeEndpoint(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Unexpected status {(int)response.StatusCode} for {path}.");
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            WebTestSupport.AssertTenantAuthorizeRedirect(response);
        }
    }

    [Fact]
    public async Task UnknownPage_SignedIn_Returns404WithNotFoundPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/definitely-not-a-page", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"not-found-heading\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_Challenge_UsesCodeFlowWithPkceStateAndNonce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account/sign-in", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        var location = response.Headers.Location;
        Assert.Equal("code", WebTestSupport.QueryValue(location, "response_type"));
        Assert.Equal("S256", WebTestSupport.QueryValue(location, "code_challenge_method"));
        Assert.False(string.IsNullOrWhiteSpace(WebTestSupport.QueryValue(location, "code_challenge")));
        Assert.False(string.IsNullOrWhiteSpace(WebTestSupport.QueryValue(location, "state")));
        Assert.False(string.IsNullOrWhiteSpace(WebTestSupport.QueryValue(location, "nonce")));
        Assert.Equal(TestWebApp.ClientId.ToString("D"), WebTestSupport.QueryValue(location, "client_id"));
        Assert.Equal("https://localhost/signin-oidc", WebTestSupport.QueryValue(location, "redirect_uri"));
        Assert.Contains(TestApi.ScopeUri, WebTestSupport.QueryValue(location, "scope")!.Split(' '));
        Assert.Null(WebTestSupport.QueryValue(location, "id_token"));
        Assert.DoesNotContain("token", WebTestSupport.QueryValue(location, "response_type"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_TwoChallenges_UseDifferentStateNonceAndChallenge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var first = (await client.GetAsync("/account/sign-in", cancellationToken)).Headers.Location;
        var second = (await client.GetAsync("/account/sign-in", cancellationToken)).Headers.Location;

        Assert.NotEqual(WebTestSupport.QueryValue(first, "state"), WebTestSupport.QueryValue(second, "state"));
        Assert.NotEqual(WebTestSupport.QueryValue(first, "nonce"), WebTestSupport.QueryValue(second, "nonce"));
        Assert.NotEqual(WebTestSupport.QueryValue(first, "code_challenge"), WebTestSupport.QueryValue(second, "code_challenge"));
    }

    [Fact]
    public async Task SignIn_Challenge_SetsSecureHttpOnlyNonceAndCorrelationCookies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account/sign-in", cancellationToken);

        var cookies = WebTestSupport.SetCookies(response);
        Assert.Contains(cookies, cookie => cookie.StartsWith(".AspNetCore.OpenIdConnect.Nonce.", StringComparison.Ordinal));
        Assert.Contains(cookies, cookie => cookie.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));
        foreach (var cookie in cookies)
        {
            var lower = cookie.ToLowerInvariant();
            Assert.Contains("; secure", lower, StringComparison.Ordinal);
            Assert.Contains("; httponly", lower, StringComparison.Ordinal);
            Assert.Contains("samesite=none", lower, StringComparison.Ordinal);
            Assert.Contains("path=/signin-oidc", lower, StringComparison.Ordinal);
            Assert.DoesNotContain("domain=", lower, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SignIn_Challenge_DoesNotIssueSessionCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/account/sign-in", cancellationToken);

        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task SignIn_NonGetMethod_DoesNotChallenge(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.SendAsync(method, "/account/sign-in", cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionCookie_Issued_IsHostPrefixedSecureHttpOnlyLax()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync($"{TestCookieSignInStartupFilter.Path}/{TestUsers.User.ObjectId:D}", cancellationToken);

        var cookie = Assert.Single(WebTestSupport.SetCookies(response), value => value.StartsWith($"{SessionCookieName}=", StringComparison.Ordinal));
        var lower = cookie.ToLowerInvariant();
        Assert.Matches(@"; path=/(;|$)", lower);
        Assert.Contains("; secure", lower, StringComparison.Ordinal);
        Assert.Contains("; httponly", lower, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", lower, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", lower, StringComparison.Ordinal);
        Assert.DoesNotContain("samesite=none", lower, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionCookie_Issued_ValueContainsNoReadableClaimsOrTokens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync($"{TestCookieSignInStartupFilter.Path}/{TestUsers.User.ObjectId:D}", cancellationToken);

        var cookie = Assert.Single(WebTestSupport.SetCookies(response), value => value.StartsWith($"{SessionCookieName}=", StringComparison.Ordinal));
        Assert.DoesNotContain(TestUsers.User.ObjectId.ToString("D"), cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestUsers.User.DisplayName, cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionCookie_Issued_TicketExpiresInEightHoursAbsolute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var issuedAt = DateTimeOffset.UtcNow;

        var response = await client.GetAsync($"{TestCookieSignInStartupFilter.Path}/{TestUsers.User.ObjectId:D}", cancellationToken);

        var cookie = Assert.Single(WebTestSupport.SetCookies(response), value => value.StartsWith($"{SessionCookieName}=", StringComparison.Ordinal));
        var value = cookie[(SessionCookieName.Length + 1)..cookie.IndexOf(';', StringComparison.Ordinal)];
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(value));
        Assert.NotNull(ticket);
        var expires = ticket.Properties.ExpiresUtc;
        Assert.NotNull(expires);
        Assert.InRange(expires.Value, issuedAt.AddHours(8).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(8).AddMinutes(1));
    }

    [Fact]
    public async Task SessionCookie_SubsequentRequest_IsNotReissued()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/", cancellationToken);

        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }

    [Fact]
    public void CookieOptions_Configured_EightHourAbsoluteLifetime()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
        Assert.Equal("/account/access-denied", options.AccessDeniedPath.Value);
        Assert.Equal(SessionCookieName, options.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
    }

    [Fact]
    public void OpenIdConnectOptions_Configured_UseCodeFlowPkceAndNoSavedTokens()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal("code", options.ResponseType);
        Assert.True(options.UsePkce);
        Assert.False(options.SaveTokens);
        Assert.False(options.MapInboundClaims);
        Assert.Equal("name", options.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
    }

    [Fact]
    public void OpenIdConnectOptions_Configured_AcceptOnlyTenantIssuer()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal([TestIdentityProvider.Issuer(TestTenant.Id)], options.TokenValidationParameters.ValidIssuers);
    }

    [Fact]
    public void TokenCache_Configured_IsEncrypted()
    {
        var options = factory.Services.GetRequiredService<IOptions<MsalDistributedTokenCacheAdapterOptions>>().Value;

        Assert.True(options.Encrypt);
    }

    [Fact]
    public async Task SignOut_Anonymous_IsChallenged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.PostAsync("/account/sign-out", new FormUrlEncodedContent([]), cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/not-found")]
    [InlineData("/account/access-denied")]
    [InlineData("/account/signed-out")]
    [InlineData("/account/sign-in-error")]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task AnonymousPages_Get_Return200(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/account/access-denied")]
    [InlineData("/account/signed-out")]
    [InlineData("/account/sign-in-error")]
    public async Task AnonymousPages_Get_ShowNoUserDataAndHaveSecurityHeaders(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        SecurityHeaderAssertions.AssertWeb(response);
        Assert.DoesNotContain("id=\"account-display-name\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"account-user-id\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sign-out-form\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticAssets_Anonymous_Return200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var home = await client.GetStringAsync("/", cancellationToken);

        var assets = WebTestSupport.LocalAssetUrls(home);

        Assert.NotEmpty(assets);
        Assert.Contains(assets, asset => asset.Contains("blazor.web", StringComparison.Ordinal));
        Assert.Contains(assets, asset => asset.Contains(".css", StringComparison.Ordinal));
        foreach (var asset in assets)
        {
            var response = await client.GetAsync(asset, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task StaticAssets_BlazorFramework_Anonymous_Returns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/_framework/blazor.web.js", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HomePage_Anonymous_ShowsSignInLinkAndNoAccountMenu()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var html = await client.GetStringAsync("/", cancellationToken);

        Assert.Contains("id=\"home-sign-in\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"sign-in-link\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sign-out-form\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sign-out-button\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"account-link\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"home-greeting\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_SignedIn_ShowsGreetingAndSignOutForm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync("/", cancellationToken);

        Assert.Contains("id=\"home-greeting\"", html, StringComparison.Ordinal);
        Assert.Contains(TestUsers.User.DisplayName, html, StringComparison.Ordinal);
        Assert.Contains("id=\"account-name\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"account-link\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"sign-out-form\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"sign-out-button\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"home-sign-in\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sign-in-link\"", html, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(WebTestSupport.ExtractSignOutAntiforgeryToken(html)));
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("action=\"/account/sign-out\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_SignedIn_ContainsNoInlineScriptOrStyle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync("/", cancellationToken);

        Assert.False(WebTestSupport.HasInlineScriptHandlerOrStyle(html));
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/account/access-denied", "access-denied-heading", "access-denied-message", "access-denied-home-link")]
    [InlineData("/account/signed-out", "signed-out-heading", "signed-out-sign-in-link", "signed-out-sign-in-link")]
    [InlineData("/account/sign-in-error", "sign-in-error-heading", "sign-in-error-message", "sign-in-error-retry-link")]
    [InlineData("/not-found", "not-found-heading", "not-found-message", "not-found-message")]
    public async Task AccountPages_Rendered_HaveStableElementIds(string path, string first, string second, string third)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var html = await client.GetStringAsync(path, cancellationToken);

        Assert.Contains($"id=\"{first}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"{second}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"{third}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlazorHub_AnonymousNegotiate_IsChallenged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", new StringContent(string.Empty), cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("connectionToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BlazorHub_AnonymousWebSocketUpgradeStyleGet_IsNotServed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/_blazor?id=abc", cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.SwitchingProtocols, response.StatusCode);
    }

    [Fact]
    public async Task WebRequest_TestHeaders_DoNotAuthenticate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        SpoofedTestHeaders.Add(client.DefaultRequestHeaders, TestUsers.Admin);

        var response = await client.GetAsync("/account", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Fact]
    public async Task WebRequest_BearerTokenForTheApi_DoesNotAuthenticate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.Admin));

        var response = await client.GetAsync("/account", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Fact]
    public async Task WebRequest_ForgedSessionCookie_DoesNotAuthenticate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add("Cookie", $"{SessionCookieName}=CfDJ8{new string('A', 200)}");

        var response = await client.SendAsync(request, cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Fact]
    public async Task WebRequest_SessionCookieWithoutHostPrefix_DoesNotAuthenticate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var signedIn = factory.CreateWebClient();
        var response = await signedIn.GetAsync($"{TestCookieSignInStartupFilter.Path}/{TestUsers.User.ObjectId:D}", cancellationToken);
        var cookie = Assert.Single(WebTestSupport.SetCookies(response), value => value.StartsWith($"{SessionCookieName}=", StringComparison.Ordinal));
        var renamed = "SharpVault=" + cookie[(SessionCookieName.Length + 1)..cookie.IndexOf(';', StringComparison.Ordinal)];
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add("Cookie", renamed);

        var result = await other.SendAsync(request, cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(result);
    }

    [Fact]
    public async Task SecurityHeaders_FormAction_AllowsOnlySelfAndTheIdentityProviderOrigin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/", cancellationToken);

        var csp = response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy)!;
        var formAction = csp.Split(';', StringSplitOptions.TrimEntries).Single(directive => directive.StartsWith("form-action ", StringComparison.Ordinal));
        Assert.Equal(["form-action", "'self'", "https://login.microsoftonline.com"], formAction.Split(' '));
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/account/sign-in")]
    [InlineData("/account/sign-in?returnUrl=%2F%2Fevil.example")]
    [InlineData("/account/sign-in-error")]
    [InlineData("/does-not-exist")]
    public async Task SecurityHeaders_ChallengeAndErrorResponses_CarryTheWebPolicy(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync(path, cancellationToken);

        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Fact]
    public async Task SecurityHeaders_SignedInPages_AreNotCacheable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var home = await client.GetAsync("/", cancellationToken);

        Assert.Contains(SecurityHeaderValues.NoStore, home.HeaderValue(SecurityHeaderNames.CacheControl), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_WebProductionWithClientSecret_FailsWithoutEchoingSecret()
    {
        var secret = "web-secret-" + Guid.NewGuid().ToString("N");
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AzureAd:ClientCredentials:0:SourceType", "ClientSecret");
            builder.UseSetting("AzureAd:ClientCredentials:0:ClientSecret", secret);
        });

        var exception = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains(Flatten(exception), candidate => candidate is InvalidOperationException);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText(secret), "A captured log entry contained the client secret.");
    }

    [Fact]
    public void Startup_WebProductionWithoutCredential_Fails()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains(Flatten(exception), candidate => candidate is InvalidOperationException);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    [InlineData("contoso.onmicrosoft.com")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public void Startup_WebNonGuidTenant_FailsWithoutEchoingTheValue(string tenant)
    {
        using var broken = factory.WithWebHostBuilder(builder => builder.UseSetting("AzureAd:TenantId", tenant));

        var exception = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        Assert.Contains(Flatten(exception), candidate => candidate is InvalidOperationException);
        if (tenant.Length > 0)
        {
            Assert.DoesNotContain($"'{tenant}'", exception.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Startup_WebHttpInstance_Fails()
    {
        using var broken = factory.WithWebHostBuilder(builder => builder.UseSetting("AzureAd:Instance", "http://login.microsoftonline.com/"));

        var exception = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        Assert.Contains(Flatten(exception), candidate => candidate is InvalidOperationException);
    }

    private static List<Exception> Flatten(Exception exception)
    {
        var chain = new List<Exception>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(current);
        }

        return chain;
    }
}
