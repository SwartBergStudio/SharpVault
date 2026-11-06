// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.UiShared.Services;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class SessionRevocationTests : IDisposable
{
    private const string ProfileJson = """{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Alice Example","isAdmin":true}""";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero));
    private readonly DistributedSessionRevocationStore _revocations = new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
    private readonly StubHttpHandler _handler = new();
    private readonly IApiAccessTokenProvider _tokens = Substitute.For<IApiAccessTokenProvider>();
    private readonly AuthenticationStateProvider _authentication = Substitute.For<AuthenticationStateProvider>();
    private readonly ClaimsPrincipal _principal;
    private readonly string _sessionId;

    public SessionRevocationTests()
    {
        var identity = new ClaimsIdentity(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"), "test");
        SessionClaimsIssuer.Add(identity, _time);
        _principal = new ClaimsPrincipal(identity);
        _sessionId = identity.FindFirst(SessionClaims.SessionId)!.Value;
        _tokens.InteractiveSignInAvailable.Returns(true);
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.Bearer("token"));
        _authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(_principal));
        _handler.Respond(HttpStatusCode.OK, ProfileJson);
    }

    public void Dispose() => _handler.Dispose();

    private ApiClientBaseTests.ProbeClient Client() =>
        new(
            new HttpClient(_handler) { BaseAddress = new Uri("https://api.example.test/") },
            _tokens,
            _authentication,
            new SessionValidator(_revocations, _time),
            NullLogger<ApiClientBaseTests.ProbeClient>.Instance);

    [Fact]
    public async Task Circuit_ActiveSession_ApiCallSucceeds()
    {
        var response = await Client().ReadAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Circuit_AfterSignOutElsewhere_ApiCallReturnsSignInRequired()
    {
        await _revocations.RevokeAsync(_sessionId, TestContext.Current.CancellationToken);

        var response = await Client().ReadAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Circuit_PastSessionExpiry_ApiCallReturnsSignInRequired()
    {
        _time.Advance(WebApp::SharpVault.Web.Security.SessionLifetime.Value + TimeSpan.FromSeconds(1));

        var response = await Client().ReadAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Circuit_OtherSessionRevoked_StaysActive()
    {
        await _revocations.RevokeAsync(Guid.NewGuid().ToString("N"), TestContext.Current.CancellationToken);

        var response = await Client().ReadAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("abc", null)]
    [InlineData(null, "99999999999")]
    [InlineData("abc", "not-a-number")]
    [InlineData("abc", "-1")]
    public async Task Validator_MissingOrMalformedSessionClaims_IsNotActive(string? sessionId, string? expires)
    {
        var claims = TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User");
        if (sessionId is not null)
        {
            claims.Add(new Claim(SessionClaims.SessionId, sessionId));
        }

        if (expires is not null)
        {
            claims.Add(new Claim(SessionClaims.ExpiresAt, expires));
        }

        var active = await new SessionValidator(_revocations, _time).IsActiveAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), TestContext.Current.CancellationToken);

        Assert.False(active);
    }

    [Fact]
    public async Task Validator_AnonymousPrincipal_IsNotActive()
    {
        var active = await new SessionValidator(_revocations, _time).IsActiveAsync(new ClaimsPrincipal(new ClaimsIdentity()), TestContext.Current.CancellationToken);

        Assert.False(active);
    }

    [Fact]
    public void Issuer_Always_ReplacesAnyForgedSessionClaims()
    {
        var identity = new ClaimsIdentity(
            [new Claim(SessionClaims.SessionId, "forged"), new Claim(SessionClaims.ExpiresAt, "99999999999")],
            "test");

        SessionClaimsIssuer.Add(identity, _time);

        Assert.Single(identity.FindAll(SessionClaims.SessionId));
        Assert.NotEqual("forged", identity.FindFirst(SessionClaims.SessionId)!.Value);
        var expires = long.Parse(identity.FindFirst(SessionClaims.ExpiresAt)!.Value, CultureInfo.InvariantCulture);
        Assert.Equal(_time.GetUtcNow().Add(WebApp::SharpVault.Web.Security.SessionLifetime.Value).ToUnixTimeSeconds(), expires);
    }
}
