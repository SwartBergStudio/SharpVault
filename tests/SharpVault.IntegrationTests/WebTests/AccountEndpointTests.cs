// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.IdentityModel.Tokens;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class AccountEndpointTests(WebTestFactory factory)
{
    private const string SessionCookieName = "__Host-SharpVault";
    private const string ErrorPath = "/account/sign-in-error";

    [Fact]
    public async Task SignOut_Valid_RedirectsToEndSessionWithFixedPostLogoutTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(client, cancellationToken);

        var response = await PostSignOutAsync(client, "/account/sign-out", token, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal("login.microsoftonline.com", location.Host);
        Assert.Equal($"/{TestTenant.Id:D}/oauth2/v2.0/logout", location.AbsolutePath);
        Assert.Equal("https://localhost/signout-callback-oidc", WebTestSupport.QueryValue(location, "post_logout_redirect_uri"));
        Assert.Contains(
            WebTestSupport.SetCookies(response),
            cookie => cookie.StartsWith($"{SessionCookieName}=;", StringComparison.Ordinal)
                && cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SignOut_Valid_EndsTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(client, cancellationToken);

        await PostSignOutAsync(client, "/account/sign-out", token, cancellationToken);
        var after = await client.GetAsync("/account", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(after);
    }

    [Fact]
    public async Task SignOut_Valid_ReplayedWithTheSameTokenIsChallenged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(client, cancellationToken);
        await PostSignOutAsync(client, "/account/sign-out", token, cancellationToken);

        var replay = await PostSignOutAsync(client, "/account/sign-out", token, cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(replay);
    }

    [Theory]
    [InlineData("/account/sign-out?returnUrl=https://evil.example")]
    [InlineData("/account/sign-out?post_logout_redirect_uri=https://evil.example")]
    [InlineData("/account/sign-out?redirect_uri=//evil.example")]
    public async Task SignOut_Valid_IgnoresAttackerSuppliedRedirectTargets(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(client, cancellationToken);

        var response = await PostSignOutAsync(client, path, token, cancellationToken, extraFields: [new("returnUrl", "https://evil.example"), new("post_logout_redirect_uri", "https://evil.example")]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.DoesNotContain("evil.example", Uri.UnescapeDataString(location), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://localhost/signout-callback-oidc", WebTestSupport.QueryValue(response.Headers.Location, "post_logout_redirect_uri"));
    }

    [Fact]
    public async Task SignOut_WithoutAntiforgeryToken_Returns400AndKeepsSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        await ReadSignOutTokenAsync(client, cancellationToken);

        var response = await client.PostAsync("/account/sign-out", new FormUrlEncodedContent([]), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(client, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_WithoutAntiforgeryTokenAndWithoutBody_Returns400AndKeepsSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        await ReadSignOutTokenAsync(client, cancellationToken);

        var response = await client.PostAsync("/account/sign-out", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(client, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_WithoutAntiforgeryCookie_Returns400AndKeepsSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(browser, cancellationToken);
        using var attacker = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await PostSignOutAsync(attacker, "/account/sign-out", token, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(attacker, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_TokenFromAnotherUsersSession_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var victim = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        using var attacker = await factory.CreateSignedInClientAsync(TestUsers.OtherUser, cancellationToken);
        var attackerToken = await ReadSignOutTokenAsync(attacker, cancellationToken);
        await ReadSignOutTokenAsync(victim, cancellationToken);

        var response = await PostSignOutAsync(victim, "/account/sign-out", attackerToken, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(victim, response, cancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("CfDJ8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task SignOut_InvalidAntiforgeryToken_Returns400AndKeepsSession(string token)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        await ReadSignOutTokenAsync(client, cancellationToken);

        var response = await PostSignOutAsync(client, "/account/sign-out", token, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(client, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_TamperedAntiforgeryToken_Returns400AndKeepsSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(client, cancellationToken);
        var tampered = token[..^4] + (token.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var response = await PostSignOutAsync(client, "/account/sign-out", tampered, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(client, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_TokenInQueryStringOnly_Returns400AndKeepsSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(client, cancellationToken);

        var response = await client.PostAsync($"/account/sign-out?{WebTestSupport.AntiforgeryFieldName}={Uri.EscapeDataString(token)}", new FormUrlEncodedContent([]), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(client, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_JsonBodyWithToken_Returns400AndKeepsSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(client, cancellationToken);
        var content = new StringContent($"{{\"{WebTestSupport.AntiforgeryFieldName}\":\"{token}\"}}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/account/sign-out", content, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSessionKeptAsync(client, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_BadRequest_ProblemResponseDoesNotLeakDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.PostAsync("/account/sign-out", new FormUrlEncodedContent([]), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("Antiforgery", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestUsers.User.ObjectId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        SecurityHeaderAssertions.AssertWeb(response);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task SignOut_NonPostRequest_DoesNotSignOut(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.SendAsync(method, "/account/sign-out", cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.Location is null || response.Headers.Location.Host != "login.microsoftonline.com",
            "A non-POST request was redirected to the end session endpoint.");
        await AssertSessionKeptAsync(client, response, cancellationToken);
    }

    [Fact]
    public async Task SignOut_Anonymous_WithAnyTokenIsChallenged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var signedIn = await factory.CreateSignedInClientAsync(TestUsers.User, cancellationToken);
        var token = await ReadSignOutTokenAsync(signedIn, cancellationToken);
        using var anonymous = factory.CreateWebClient();

        var response = await PostSignOutAsync(anonymous, "/account/sign-out", token, cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Fact]
    public async Task SignInCallback_ForgedState_RedirectsToErrorPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.PostAsync("/signin-oidc", Form(("code", "x"), ("state", "garbage")), cancellationToken);

        AssertErrorRedirect(response);
    }

    [Fact]
    public async Task SignInCallback_ErrorWithDescription_RedirectsToConstantErrorPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        const string payload = "<script>alert(1)</script>";

        var response = await client.PostAsync(
            "/signin-oidc",
            Form(("error", "access_denied"), ("error_description", payload), ("state", "garbage")),
            cancellationToken);

        AssertErrorRedirect(response);
        Assert.DoesNotContain("script", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_denied", response.Headers.Location.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task SignInCallback_ErrorAfterRealChallenge_RedirectsToConstantErrorPageWithoutSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var challenge = await client.GetAsync("/account/sign-in", cancellationToken);
        var state = WebTestSupport.QueryValue(challenge.Headers.Location, "state")!;

        var response = await client.PostAsync(
            "/signin-oidc",
            Form(("error", "access_denied"), ("error_description", "AADSTS50105: the user <b>bob@contoso.com</b> is blocked"), ("state", state)),
            cancellationToken);

        AssertErrorRedirect(response);
        Assert.DoesNotContain("AADSTS", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SignInCallback_GetWithErrorQuery_DoesNotReflectInput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync("/signin-oidc?error=access_denied&error_description=%3Cscript%3Ealert(1)%3C/script%3E&state=garbage", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("alert(1)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(1)", response.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SignInCallback_ErrorWithoutAnyState_RedirectsToErrorPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.PostAsync("/signin-oidc", Form(("error", "server_error")), cancellationToken);

        AssertErrorRedirect(response);
    }

    [Fact]
    public async Task SignInCallback_EmptyPost_RedirectsToErrorPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.PostAsync("/signin-oidc", Form(), cancellationToken);

        AssertErrorRedirect(response);
    }

    [Fact]
    public async Task SignInErrorPage_QueryText_IsNotRendered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await client.GetAsync($"{ErrorPath}?error_description=<b>pwned</b>&error=<script>alert(1)</script>&message=pwned", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("pwned", html, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(1)", html, StringComparison.Ordinal);
        Assert.Contains("id=\"sign-in-error-message\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInErrorPage_RetryLink_PointsAtTheSignInEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var html = await client.GetStringAsync(ErrorPath, cancellationToken);

        Assert.Contains("id=\"sign-in-error-retry-link\" href=\"/account/sign-in\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInCallback_IdTokenFromTheConfiguredTenant_IsNotRejectedByTheTenantCheck()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        factory.Logs.Clear();

        var response = await PostIdTokenAsync(client, (builder, _) => builder, cancellationToken);

        Assert.False(
            factory.Logs.Entries.Any(entry => entry.Message.Contains("Sign-in rejected by the ID token check", StringComparison.Ordinal)),
            "A token from the configured tenant was rejected by the ID token check.");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task SignInCallback_IdTokenWithForeignTenantIdAndOurIssuer_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        factory.Logs.Clear();

        var response = await PostIdTokenAsync(
            client,
            (builder, _) => builder.WithClaim("tid", TestTenant.OtherId.ToString("D")),
            cancellationToken);

        AssertRejectedSignIn(response);
        Assert.Contains(
            factory.Logs.Entries,
            entry => entry.Message.Contains("Sign-in rejected by the ID token check", StringComparison.Ordinal)
                && entry.Message.Contains("WrongTenant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SignInCallback_IdTokenWithoutTenantId_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(client, (builder, _) => builder.WithoutClaim("tid"), cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task SignInCallback_IdTokenWithInvalidObjectId_IsRejected(string oid)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(client, (builder, _) => builder.WithClaim("oid", oid), cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_IdTokenIssuedByAnotherTenant_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(
            client,
            (builder, _) => builder
                .WithClaim("iss", TestIdentityProvider.Issuer(TestTenant.OtherId))
                .WithClaim("tid", TestTenant.OtherId.ToString("D")),
            cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_IdTokenFromCommonIssuer_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(
            client,
            (builder, _) => builder.WithClaim("iss", $"{TestIdentityProvider.Instance}common/v2.0"),
            cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_IdTokenForAnotherClient_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(client, (builder, _) => builder.WithClaim("aud", TestApi.ClientId.ToString("D")), cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_IdTokenSignedWithAnotherKey_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(
            client,
            (builder, _) => builder.SignedWith(TestSigningKeys.OtherWithSameKeyId, SecurityAlgorithms.RsaSha256),
            cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_ExpiredIdToken_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(
            client,
            (builder, _) => builder.NotBefore(DateTimeOffset.UtcNow.AddHours(-3)).ExpiresAt(DateTimeOffset.UtcNow.AddHours(-2)),
            cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_IdTokenWithWrongNonce_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(client, (builder, _) => builder.WithClaim("nonce", "attacker-nonce"), cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_IdTokenWithoutNonce_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostIdTokenAsync(client, (builder, _) => builder.WithoutClaim("nonce"), cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_UnsignedIdToken_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();

        var response = await PostRawIdTokenAsync(client, ForgedTokens.AlgNone(), cancellationToken);

        AssertRejectedSignIn(response);
    }

    [Fact]
    public async Task SignInCallback_RejectedIdToken_DoesNotLogNamesEmailsOrTokens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        factory.Logs.Clear();
        string? token = null;

        await PostIdTokenAsync(
            client,
            (builder, _) =>
            {
                token = builder.WithClaim("tid", TestTenant.OtherId.ToString("D")).Build();
                return builder;
            },
            cancellationToken);

        Assert.NotNull(token);
        Assert.False(factory.Logs.ContainsText(token), "The rejected ID token was logged.");
        foreach (var segment in token.Split('.').Where(part => part.Length > 20))
        {
            Assert.False(factory.Logs.ContainsText(segment), "A segment of the rejected ID token was logged.");
        }

        Assert.False(factory.Logs.ContainsText(TestUsers.User.DisplayName), "The display name was logged.");
        Assert.False(factory.Logs.ContainsText(TestUsers.User.Email!), "The email address was logged.");
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static void AssertErrorRedirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(ErrorPath, response.Headers.Location.OriginalString);
        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }

    private static void AssertRejectedSignIn(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ErrorPath, response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }

    private static async Task<string> ReadSignOutTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var html = await client.GetStringAsync("/", cancellationToken);
        var token = WebTestSupport.ExtractSignOutAntiforgeryToken(html);
        Assert.False(string.IsNullOrEmpty(token), "The sign-out form did not contain an antiforgery token.");
        return token;
    }

    private static Task<HttpResponseMessage> PostSignOutAsync(
        HttpClient client,
        string path,
        string token,
        CancellationToken cancellationToken,
        IEnumerable<KeyValuePair<string, string>>? extraFields = null)
    {
        var fields = new List<KeyValuePair<string, string>> { new(WebTestSupport.AntiforgeryFieldName, token) };
        if (extraFields is not null)
        {
            fields.AddRange(extraFields);
        }

        return client.PostAsync(path, new FormUrlEncodedContent(fields), cancellationToken);
    }

    private static async Task AssertSessionKeptAsync(HttpClient client, HttpResponseMessage failed, CancellationToken cancellationToken)
    {
        Assert.DoesNotContain(
            WebTestSupport.SetCookies(failed),
            cookie => cookie.StartsWith($"{SessionCookieName}=;", StringComparison.Ordinal));
        var home = await client.GetStringAsync("/", cancellationToken);
        Assert.Contains("id=\"home-greeting\"", home, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> PostIdTokenAsync(
        HttpClient client,
        Func<TestTokenBuilder, string, TestTokenBuilder> customize,
        CancellationToken cancellationToken)
    {
        var challenge = await client.GetAsync("/account/sign-in", cancellationToken);
        var state = WebTestSupport.QueryValue(challenge.Headers.Location, "state")!;
        var nonce = WebTestSupport.QueryValue(challenge.Headers.Location, "nonce")!;
        var builder = TestTokens.For(TestUsers.User)
            .WithClaim("aud", TestWebApp.ClientId.ToString("D"))
            .WithClaim("nonce", nonce)
            .WithoutClaim("scp");
        var idToken = customize(builder, nonce).Build();

        return await client.PostAsync("/signin-oidc", Form(("state", state), ("id_token", idToken)), cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostRawIdTokenAsync(HttpClient client, string idToken, CancellationToken cancellationToken)
    {
        var challenge = await client.GetAsync("/account/sign-in", cancellationToken);
        var state = WebTestSupport.QueryValue(challenge.Headers.Location, "state")!;
        return await client.PostAsync("/signin-oidc", Form(("state", state), ("id_token", idToken)), cancellationToken);
    }
}
