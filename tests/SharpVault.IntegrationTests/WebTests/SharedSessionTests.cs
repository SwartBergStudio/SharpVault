// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.Authentication.Local;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Auth;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed partial class SharedSessionTests(LocalApiTestFactory api) : IDisposable
{
    private const string PickerPath = "/account/local-sign-in";
    private const string SignOutPath = "/account/sign-out";
    private const string SessionCookie = "__Host-SharpVault";
    private const string TokenField = "__RequestVerificationToken";
    private const string LockReason = "Dismissed by the owner in the shared session test";
    private static readonly TimeSpan RevokeLimit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SignOutMargin = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SharedRevokeLimit = TimeSpan.FromSeconds(8);

    private readonly List<LocalWebTestFactory> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        _hosts.Clear();
    }

    [Fact]
    public async Task SignOut_OnOneInstance_EndsTheSessionOnAnotherInstance()
    {
        var first = NewHost();
        var second = NewHost();
        await api.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        var session = await SignInAsync(first, LocalAccounts.UserOne);
        var principal = Unprotect(first, session.Cookie);

        var response = await SignOutAsync(first, session);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = second.Services.CreateScope();
        var revalidator = scope.ServiceProvider.GetRequiredService<SessionRevalidator>();
        Assert.False(await revalidator.IsStillValidAsync(principal, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revalidation_UserLockedOnTheApi_EndsTheCircuit()
    {
        var web = NewHost();
        await api.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        var session = await SignInAsync(web, LocalAccounts.UserOne);
        var principal = Unprotect(web, session.Cookie);
        using (var admin = WebCircuit.For(web.Services, LocalPrincipalFactory.Create(LocalAccounts.AdminOne, "LocalAccount")))
        {
            var locked = await admin.AdminUsers.LockAsync(
                LocalAccounts.UserOne.Id,
                new LockUserRequestDto { Category = ReasonCategoryDto.Dismissed, Reason = LockReason },
                TestContext.Current.CancellationToken);
            Assert.Equal(ApiCallStatus.Success, locked.Status);
        }

        using var scope = web.Services.CreateScope();
        var revalidator = scope.ServiceProvider.GetRequiredService<SessionRevalidator>();
        var validator = scope.ServiceProvider.GetRequiredService<ISessionValidator>();
        Assert.False(await revalidator.IsStillValidAsync(principal, TestContext.Current.CancellationToken));
        Assert.False(await validator.IsActiveAsync(principal, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revocation_SurvivesAWebRestart()
    {
        var first = NewHost();
        await api.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        var session = await SignInAsync(first, LocalAccounts.UserOne);
        var principal = Unprotect(first, session.Cookie);
        await SignOutAsync(first, session);
        first.Dispose();

        var restarted = NewHost();
        using var scope = restarted.Services.CreateScope();
        var revalidator = scope.ServiceProvider.GetRequiredService<SessionRevalidator>();

        Assert.False(await revalidator.IsStillValidAsync(principal, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SignOut_ApiUnavailable_EndsTheLocalSessionWithinTheLimit()
    {
        var web = new LocalWebTestFactory();
        _hosts.Add(web);
        await api.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        var session = await SignInAsync(web, LocalAccounts.UserOne);
        var principal = Unprotect(web, session.Cookie);

        var stopwatch = Stopwatch.StartNew();
        var response = await SignOutAsync(web, session);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(stopwatch.Elapsed < SharedRevokeLimit, $"The sign-out took {stopwatch.Elapsed}, over the {SharedRevokeLimit} limit.");
        using var scope = web.Services.CreateScope();
        var revalidator = scope.ServiceProvider.GetRequiredService<SessionRevalidator>();
        Assert.False(await revalidator.IsStillValidAsync(principal, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LockedUser_NextCircuitCall_ReturnsAccessDenied()
    {
        var web = NewHost();
        await api.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        using (var admin = WebCircuit.For(web.Services, LocalPrincipalFactory.Create(LocalAccounts.AdminOne, "LocalAccount")))
        {
            var locked = await admin.AdminUsers.LockAsync(
                LocalAccounts.UserOne.Id,
                new LockUserRequestDto { Category = ReasonCategoryDto.Dismissed, Reason = LockReason },
                TestContext.Current.CancellationToken);
            Assert.Equal(ApiCallStatus.Success, locked.Status);
        }

        using var user = WebCircuit.For(web.Services, LocalPrincipalFactory.Create(LocalAccounts.UserOne, "LocalAccount"));
        var response = await user.Me.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.AccessDenied, response.Status);
    }

    [Fact]
    public async Task SignedInStaticPage_IdleBootCarriesOnlyTheExpiryAsSessionTag()
    {
        var web = NewHost();
        await api.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        var session = await SignInAsync(web, LocalAccounts.UserOne);
        var principal = Unprotect(web, session.Cookie);

        var html = await GetSignedInPageAsync(web, session);

        var boot = Regex.Match(html, "<script id=\"idle-boot\"[^>]*>", RegexOptions.CultureInvariant);
        Assert.True(boot.Success, "The signed-in page has no idle boot script.");
        var expiry = principal.FindFirst(SessionClaims.ExpiresAt)?.Value;
        Assert.NotNull(expiry);
        Assert.Contains($"data-idle-session=\"{expiry}\"", boot.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(principal.FindFirst(SessionClaims.SessionId)!.Value, html, StringComparison.Ordinal);
    }

    private static async Task<string> GetSignedInPageAsync(LocalWebTestFactory web, (string Cookie, string Antiforgery) session)
    {
        using var client = web.CreateWebClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, WebTestSupport.SignedInStaticPage);
        request.Headers.Add("Cookie", session.Cookie + "; " + session.Antiforgery);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SignOut_SharedApiHangs_ReturnsWithinTheRevokeLimit()
    {
        var web = new LocalWebTestFactory();
        _hosts.Add(web);
        web.SharedSessionHandler = new HangingHandler();
        var session = await SignInAsync(web, LocalAccounts.UserOne);
        var stopwatch = Stopwatch.StartNew();

        var response = await SignOutAsync(web, session);

        stopwatch.Stop();
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(stopwatch.Elapsed < RevokeLimit + SignOutMargin, $"Sign-out took {stopwatch.Elapsed.TotalSeconds:F1} s.");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private LocalWebTestFactory NewHost()
    {
        var host = new LocalWebTestFactory();
        host.UseApi(api);
        _hosts.Add(host);
        return host;
    }

    private static string CookieValue(HttpResponseMessage response)
    {
        var values = response.Headers.TryGetValues("Set-Cookie", out var found) ? found : [];
        var cookie = Assert.Single(values, value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        return cookie[(SessionCookie.Length + 1)..].Split(';')[0];
    }

    private static async Task<(string Token, string Cookie)> FetchFormAsync(HttpClient client)
    {
        var response = await client.GetAsync(PickerPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = WebUtility.HtmlDecode(TokenPattern().Match(html).Groups["token"].Value);
        var values = response.Headers.TryGetValues("Set-Cookie", out var found) ? found : [];
        var cookie = Assert.Single(values, value => value.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
        return (token, cookie.Split(';')[0]);
    }

    private static async Task<(string Cookie, string Antiforgery)> SignInAsync(LocalWebTestFactory web, LocalAccount account)
    {
        using var client = web.CreateWebClient();
        var form = await FetchFormAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [TokenField] = form.Token,
                ["accountId"] = account.Id.ToString("D"),
                ["returnUrl"] = "/",
            }),
        };
        request.Headers.Add("Cookie", form.Cookie);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (SessionCookie + "=" + CookieValue(response), form.Cookie);
    }

    private static async Task<HttpResponseMessage> SignOutAsync(LocalWebTestFactory web, (string Cookie, string Antiforgery) session)
    {
        using var client = web.CreateWebClient();
        using var shell = new HttpRequestMessage(HttpMethod.Get, WebTestSupport.SignedInStaticPage);
        shell.Headers.Add("Cookie", session.Cookie + "; " + session.Antiforgery);
        var home = await client.SendAsync(shell, TestContext.Current.CancellationToken);
        var html = await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var form = Regex.Match(html, "<form[^>]*id=\"sign-out-form\".*?</form>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(form.Success);
        var fields = new Dictionary<string, string>
        {
            [TokenField] = WebUtility.HtmlDecode(TokenPattern().Match(form.Value).Groups["token"].Value),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, SignOutPath) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Cookie", session.Cookie + "; " + session.Antiforgery);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static ClaimsPrincipal Unprotect(LocalWebTestFactory web, string sessionCookie)
    {
        var options = web.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var value = sessionCookie[(SessionCookie.Length + 1)..];
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(value));
        return Assert.IsType<AuthenticationTicket>(ticket).Principal;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*\\bvalue=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
