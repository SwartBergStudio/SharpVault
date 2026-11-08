// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed partial class LocalSignInTests : IAsyncLifetime
{
    private const string PickerPath = "/account/local-sign-in";
    private const string SignOutPath = "/account/sign-out";
    private const string SessionCookie = "__Host-SharpVault";
    private const string TokenField = "__RequestVerificationToken";

    private readonly LocalWebTestFactory _web;
    private readonly LocalApiTestFactory _api;

    public LocalSignInTests(LocalApiTestFactory api, LocalWebTestFactory web)
    {
        _api = api;
        _web = web;
        _web.UseApi(_api);
    }

    public ValueTask InitializeAsync()
    {
        _api.StorageRequests.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(_api.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    public static TheoryData<string> MalformedAccountIds() =>
    [
        string.Empty,
        "not-a-guid",
        "5a1d000000004000800000000000002",
        "{5a1d0000-0000-4000-8000-000000000021}",
        "00000000-0000-0000-0000-000000000000",
    ];

    public static TheoryData<string> ReturnUrlsThatMustBeRejected() =>
    [
        "//evil.example",
        "/\\evil.example",
        "javascript:alert(1)",
        "/ok\r\nSet-Cookie: x=y",
    ];

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*\\bvalue=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();

    [GeneratedRegex("value=\"[0-9a-fA-F-]{36}\"", RegexOptions.CultureInvariant)]
    private static partial Regex GuidValueAttribute();

    [Fact]
    public async Task GetSignIn_Anonymous_RedirectsToThePickerWithSanitizedReturnUrl()
    {
        using var client = _web.CreateWebClient();

        var unsafeResponse = await client.GetAsync("/account/sign-in?returnUrl=//evil.example", TestContext.Current.CancellationToken);
        var safeResponse = await client.GetAsync("/account/sign-in?returnUrl=%2Faccount", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, unsafeResponse.StatusCode);
        Assert.Equal(PickerPath + "?returnUrl=%2F", unsafeResponse.Headers.Location?.OriginalString);
        Assert.Equal(PickerPath + "?returnUrl=%2Faccount", safeResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetPicker_Loopback_Returns200WithFourAccountForms()
    {
        using var client = _web.CreateWebClient();

        var response = await client.GetAsync(PickerPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (var slug in new[] { "admin-one", "admin-two", "user-one", "user-two" })
        {
            Assert.Contains($"id=\"local-account-{slug}\"", html, StringComparison.Ordinal);
        }

        Assert.Equal(4, TokenPattern().Count(html));
    }

    [Fact]
    public async Task GetPicker_Loopback_HasNoRedundantSignInButtonInTheHeader()
    {
        using var client = _web.CreateWebClient();

        var html = await client.GetStringAsync(PickerPath, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("id=\"sign-in-link\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPicker_ShowsNamesAndRolesButNoGuidsInUrls()
    {
        using var client = _web.CreateWebClient();

        var html = await (await client.GetAsync(PickerPath, TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var account in LocalAccounts.All)
        {
            Assert.Contains(account.DisplayName, html, StringComparison.Ordinal);
            Assert.Contains(string.Join(", ", account.Roles), html, StringComparison.Ordinal);
        }

        var withoutFormValues = GuidValueAttribute().Replace(html, string.Empty);
        Assert.DoesNotMatch(GuidPattern(), withoutFormValues);
    }

    [Fact]
    public async Task PostPicker_ValidAccount_SetsHostPrefixedSecureHttpOnlyLaxCookieAndRedirects()
    {
        using var client = _web.CreateWebClient();

        var response = await PostAsync(client, LocalAccounts.UserOne.Id.ToString("D"), "/account");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostPicker_Cookie_ValueContainsNoReadableClaimsOrTokens()
    {
        using var client = _web.CreateWebClient();

        var response = await PostAsync(client, LocalAccounts.AdminOne.Id.ToString("D"), "/");

        var value = CookieValue(response);
        Assert.DoesNotContain(LocalAccounts.AdminOne.DisplayName, value, StringComparison.Ordinal);
        Assert.DoesNotContain(LocalAccounts.AdminOne.Id.ToString("D"), value, StringComparison.Ordinal);
        Assert.False(WebTestSupport.ContainsJwt(value), "The cookie contains a JWT-shaped string.");
        Assert.DoesNotContain("SharpVault.Admin", value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostPicker_WithoutAntiforgeryToken_Returns400AndSetsNoCookie()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);

        var response = await SendFormAsync(client, form.Cookie, new Dictionary<string, string>
        {
            ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            ["returnUrl"] = "/",
        });

        AssertRejected(response);
    }

    [Fact]
    public async Task PostPicker_WithoutAntiforgeryCookie_Returns400()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);

        var response = await SendFormAsync(client, null, new Dictionary<string, string>
        {
            [TokenField] = form.Token,
            ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            ["returnUrl"] = "/",
        });

        AssertRejected(response);
    }

    [Fact]
    public async Task PostPicker_TamperedAntiforgeryToken_Returns400()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);
        var tampered = form.Token[..^4] + (form.Token.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var response = await SendFormAsync(client, form.Cookie, new Dictionary<string, string>
        {
            [TokenField] = tampered,
            ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            ["returnUrl"] = "/",
        });

        AssertRejected(response);
    }

    [Fact]
    public async Task PostPicker_TokenInQueryOnly_Returns400()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath + "?" + TokenField + "=" + Uri.EscapeDataString(form.Token))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            }),
        };
        request.Headers.Add("Cookie", form.Cookie);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        AssertRejected(response);
    }

    [Fact]
    public async Task PostPicker_UnknownAccountId_Returns400AndSetsNoCookie()
    {
        using var client = _web.CreateWebClient();

        var response = await PostAsync(client, Guid.NewGuid().ToString("D"), "/");

        AssertRejected(response);
    }

    [Theory]
    [MemberData(nameof(MalformedAccountIds))]
    public async Task PostPicker_MalformedOrEmptyAccountId_Returns400(string accountId)
    {
        using var client = _web.CreateWebClient();

        var response = await PostAsync(client, accountId, "/");

        AssertRejected(response);
    }

    [Fact]
    public async Task PostPicker_DuplicateAccountIdFields_Returns400()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
        {
            Content = new FormUrlEncodedContent(
            [
                KeyValuePair.Create(TokenField, form.Token),
                KeyValuePair.Create("accountId", LocalAccounts.UserOne.Id.ToString("D")),
                KeyValuePair.Create("accountId", LocalAccounts.AdminOne.Id.ToString("D")),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        AssertRejected(response);
    }

    [Fact]
    public async Task PostPicker_FormSuppliesRolesOrName_AreIgnored()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);

        var response = await SendFormAsync(client, form.Cookie, new Dictionary<string, string>
        {
            [TokenField] = form.Token,
            ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            ["returnUrl"] = "/",
            ["roles"] = "SharpVault.Admin",
            ["name"] = "Mallory",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var principal = Unprotect(CookieValue(response)).Principal;
        Assert.Equal("User One", principal.FindFirst("name")?.Value);
        Assert.Equal(["SharpVault.User"], principal.FindAll("roles").Select(claim => claim.Value));
    }

    [Theory]
    [MemberData(nameof(ReturnUrlsThatMustBeRejected))]
    public async Task PostPicker_ReturnUrlVariants_RedirectOnlyToLocalPaths(string returnUrl)
    {
        using var client = _web.CreateWebClient();

        var response = await PostAsync(client, LocalAccounts.UserOne.Id.ToString("D"), returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostPicker_FormValueWithNullCharacter_Returns400AndSetsNoCookie()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);

        var response = await SendFormAsync(client, form.Cookie, new Dictionary<string, string>
        {
            [TokenField] = form.Token,
            ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            ["returnUrl"] = "/a\u0000b",
        });

        AssertRejected(response);
    }

    [Fact]
    public async Task SignOut_FormValueWithNullCharacter_Returns400AndKeepsSession()
    {
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);
        using var request = new HttpRequestMessage(HttpMethod.Post, SignOutPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { [TokenField] = "a\u0000b" }),
        };
        request.Headers.Add("Cookie", session.Cookie + "; " + session.Antiforgery);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostPicker_HugeBody_IsRejected()
    {
        using var client = _web.CreateWebClient();
        var form = await FetchFormAsync(client);

        var response = await SendFormAsync(client, form.Cookie, new Dictionary<string, string>
        {
            [TokenField] = form.Token,
            ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            ["returnUrl"] = new string('a', 6 * 1024 * 1024),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostPicker_ResponseBody_LeaksNoDetails()
    {
        using var client = _web.CreateWebClient();

        var response = await PostAsync(client, "5a1d0000-0000-4000-8000-0000000000aa", "/");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("5a1d0000", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at SharpVault.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Picker_NonLoopbackRemote_Returns404()
    {
        using var client = _web.CreateWebClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, PickerPath);
        request.Headers.Add(LoopbackStartupFilter.RemoteAddressHeader, "203.0.113.9");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Picker_PublicHostHeader_Returns404()
    {
        using var client = _web.CreateWebClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, PickerPath);
        request.Headers.Host = "sharpvault.example.com";

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("Forwarded")]
    public async Task Picker_ForwardedHeader_Returns404(string header)
    {
        using var client = _web.CreateWebClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, PickerPath);
        request.Headers.Add(header, "203.0.113.9");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostPicker_NonLoopbackRemote_Returns404AndSetsNoCookie()
    {
        using var client = _web.CreateWebClient();

        var response = await PostValidFormAsync(client, request => request.Headers.Add(LoopbackStartupFilter.RemoteAddressHeader, "203.0.113.9"));

        AssertHidden(response);
    }

    [Fact]
    public async Task PostPicker_PublicHostHeader_Returns404AndSetsNoCookie()
    {
        using var client = _web.CreateWebClient();

        var response = await PostValidFormAsync(client, request => request.Headers.Host = "sharpvault.example.com");

        AssertHidden(response);
    }

    [Fact]
    public async Task PostPicker_NonLoopbackWithoutAntiforgery_Returns404NotBadRequest()
    {
        using var client = _web.CreateWebClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["accountId"] = LocalAccounts.AdminOne.Id.ToString("D") }),
        };
        request.Headers.Add(LoopbackStartupFilter.RemoteAddressHeader, "203.0.113.9");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        AssertHidden(response);
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("Forwarded")]
    public async Task PostPicker_ForwardedHeader_Returns404AndSetsNoCookie(string header)
    {
        using var client = _web.CreateWebClient();

        var response = await PostValidFormAsync(client, request => request.Headers.Add(header, "203.0.113.9"));

        AssertHidden(response);
    }

    [Fact]
    public async Task Picker_ForwardedHeadersModeEnabled_SpoofedLoopbackGet_Returns404()
    {
        using var factory = ForwardedHeadersFactory();
        using var client = ClientFor(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, PickerPath);
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostPicker_ForwardedHeadersModeEnabled_SpoofedLoopback_Returns404AndSetsNoCookie()
    {
        using var factory = ForwardedHeadersFactory();
        using var client = ClientFor(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["accountId"] = LocalAccounts.AdminOne.Id.ToString("D"),
                ["returnUrl"] = "/",
            }),
        };
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Picker_ForwardedHeadersModeEnabled_PlainLoopbackGet_Returns404()
    {
        using var factory = ForwardedHeadersFactory();
        using var client = ClientFor(factory);

        var response = await client.GetAsync(PickerPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private WebApplicationFactory<WebApp::Program> ForwardedHeadersFactory() =>
        _web.WithWebHostBuilder(builder => builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true"));

    private static HttpClient ClientFor(WebApplicationFactory<WebApp::Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    [Fact]
    public async Task SignOut_Valid_ClearsTheCookieAndRedirectsToSignedOut()
    {
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);

        var response = await PostSignOutAsync(client, session, withToken: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/signed-out", response.Headers.Location?.OriginalString);
        var cleared = Assert.Single(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignedOutPage_AfterSignOut_ShowsAnonymousState()
    {
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);
        await PostSignOutAsync(client, session, withToken: true);

        var signedOut = await client.GetAsync("/account/signed-out", TestContext.Current.CancellationToken);
        var account = await client.GetAsync("/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        var html = await signedOut.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("id=\"sign-out-form\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, account.StatusCode);
        Assert.Contains("/account/sign-in", account.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOut_WithoutAntiforgery_Returns400AndKeepsSession()
    {
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);

        var response = await PostSignOutAsync(client, session, withToken: false);
        using var account = new HttpRequestMessage(HttpMethod.Get, "/account");
        account.Headers.Add("Cookie", session.Cookie);
        var after = await client.SendAsync(account, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task PostPicker_Cookie_CarriesAFreshSessionIdAndAnEightHourExpiry()
    {
        using var client = _web.CreateWebClient();

        var first = Unprotect(CookieValue(await PostAsync(client, LocalAccounts.UserOne.Id.ToString("D"), "/"))).Principal;
        var second = Unprotect(CookieValue(await PostAsync(client, LocalAccounts.UserOne.Id.ToString("D"), "/"))).Principal;

        var firstId = first.FindFirst(SessionClaims.SessionId)?.Value;
        Assert.False(string.IsNullOrWhiteSpace(firstId));
        Assert.NotEqual(firstId, second.FindFirst(SessionClaims.SessionId)?.Value);
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(first.FindFirst(SessionClaims.ExpiresAt)!.Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.InRange((expires - DateTimeOffset.UtcNow).TotalMinutes, 8 * 60 - 2, 8 * 60 + 1);
    }

    [Fact]
    public async Task SignOut_Valid_RevokesTheSessionSoOtherCircuitsEnd()
    {
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);
        var principal = Unprotect(session.Cookie[(SessionCookie.Length + 1)..]).Principal;
        var validator = _web.Services.GetRequiredService<ISessionValidator>();
        Assert.True(await validator.IsActiveAsync(principal, TestContext.Current.CancellationToken));

        var response = await PostSignOutAsync(client, session, withToken: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await validator.IsActiveAsync(principal, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SignOut_Anonymous_IsChallenged()
    {
        using var client = _web.CreateWebClient();

        var response = await client.PostAsync(SignOutPath, new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/sign-in", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_SignedInThroughThePicker_ServesTheShellAndTheRealApiReturnsTheProfile()
    {
        await _api.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add("Cookie", session.Cookie);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        using var circuit = WebCircuit.For(_web.Services, LocalPrincipalFactory.Create(LocalAccounts.UserOne, "LocalAccount"));
        var profile = await circuit.Me.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApiCallStatus.Success, profile.Status);
        Assert.Equal("User One", profile.Value!.DisplayName);
        Assert.Equal(LocalAccounts.UserOne.Id, profile.Value.UserId);
        Assert.False(profile.Value.IsAdmin);
    }

    [Fact]
    public async Task Account_WebHostBuiltWithoutAnApi_FailsFastInsteadOfCallingTheNetwork()
    {
        using var web = new LocalWebTestFactory();
        using var circuit = WebCircuit.For(web.Services, LocalPrincipalFactory.Create(LocalAccounts.UserOne, "LocalAccount"));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var response = await circuit.Me.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        Assert.Contains(web.Logs.Entries, entry => entry.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"The call took {stopwatch.Elapsed.TotalSeconds:F1} s.");
    }

    [Fact]
    public async Task LocalMode_WebRequests_LogNoTokensOrKeys()
    {
        _web.Logs.Clear();
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.AdminOne);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add("Cookie", session.Cookie);
        await client.SendAsync(request, TestContext.Current.CancellationToken);

        var key = Convert.ToBase64String(_web.Services.GetRequiredService<LocalSigningKey>().Key.Key);
        Assert.False(_web.Logs.ContainsText(key));
        Assert.False(WebTestSupport.LogsContainJwt(_web.Logs), "A JWT-shaped string was logged.");
        Assert.False(_web.Logs.ContainsText(CookieValue(await PostAsync(client, LocalAccounts.UserTwo.Id.ToString("D"), "/"))));
    }

    private static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

    private static string CookieValue(HttpResponseMessage response)
    {
        var cookie = Assert.Single(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        return cookie[(SessionCookie.Length + 1)..].Split(';')[0];
    }

    private static void AssertHidden(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> PostValidFormAsync(HttpClient client, Action<HttpRequestMessage> configure)
    {
        var form = await FetchFormAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [TokenField] = form.Token,
                ["accountId"] = LocalAccounts.AdminOne.Id.ToString("D"),
                ["returnUrl"] = "/",
            }),
        };
        request.Headers.Add("Cookie", form.Cookie);
        configure(request);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static void AssertRejected(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    private Microsoft.AspNetCore.Authentication.AuthenticationTicket Unprotect(string cookieValue)
    {
        var options = _web.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookieValue));
        return Assert.IsType<Microsoft.AspNetCore.Authentication.AuthenticationTicket>(ticket);
    }

    private static async Task<(string Token, string Cookie)> FetchFormAsync(HttpClient client)
    {
        var response = await client.GetAsync(PickerPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = WebUtility.HtmlDecode(TokenPattern().Match(html).Groups["token"].Value);
        var cookie = Assert.Single(SetCookies(response), value => value.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
        return (token, cookie.Split(';')[0]);
    }

    private static async Task<HttpResponseMessage> SendFormAsync(HttpClient client, string? cookie, Dictionary<string, string> fields)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath) { Content = new FormUrlEncodedContent(fields) };
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string accountId, string returnUrl)
    {
        var form = await FetchFormAsync(client);
        return await SendFormAsync(client, form.Cookie, new Dictionary<string, string>
        {
            [TokenField] = form.Token,
            ["accountId"] = accountId,
            ["returnUrl"] = returnUrl,
        });
    }

    private static async Task<(string Cookie, string Antiforgery)> SignInAsync(HttpClient client, LocalAccount account)
    {
        var form = await FetchFormAsync(client);
        var response = await SendFormAsync(client, form.Cookie, new Dictionary<string, string>
        {
            [TokenField] = form.Token,
            ["accountId"] = account.Id.ToString("D"),
            ["returnUrl"] = "/",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (SessionCookie + "=" + CookieValue(response), form.Cookie);
    }

    private static async Task<HttpResponseMessage> PostSignOutAsync(HttpClient client, (string Cookie, string Antiforgery) session, bool withToken)
    {
        var fields = new Dictionary<string, string>();
        if (withToken)
        {
            using var shell = new HttpRequestMessage(HttpMethod.Get, WebTestSupport.SignedInStaticPage);
            shell.Headers.Add("Cookie", session.Cookie + "; " + session.Antiforgery);
            var home = await client.SendAsync(shell, TestContext.Current.CancellationToken);
            var html = await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var form = Regex.Match(html, "<form[^>]*id=\"sign-out-form\".*?</form>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            Assert.True(form.Success);
            fields[TokenField] = WebUtility.HtmlDecode(TokenPattern().Match(form.Value).Groups["token"].Value);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, SignOutPath) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Cookie", session.Cookie + "; " + session.Antiforgery);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
