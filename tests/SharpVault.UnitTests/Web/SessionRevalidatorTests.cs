// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Idle;
using SharpVault.UnitTests.Support;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class SessionRevalidatorTests
{
    private static readonly TimeSpan IdleLimit = IdleLockOptions.DefaultTimeout + SessionRevalidator.ServerIdleMargin;
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SessionLength = WebApp::SharpVault.Web.Security.SessionLifetime.Value;

    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity(
        [
            new Claim(SessionClaims.SessionId, Guid.NewGuid().ToString("N")),
            new Claim(SessionClaims.ExpiresAt, DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ],
        "Test"));

    [Fact]
    public async Task Revalidator_NoInboundActivityPastTimeoutPlusMargin_EndsAndSharesTheRevocation()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shared = ActiveSharedClient();
        var harness = Create(clock, shared);
        clock.Advance(IdleLimit);

        var valid = await harness.Sut.IsStillValidAsync(Principal, Xunit.TestContext.Current.CancellationToken);

        Assert.False(valid);
        await shared.Received(1).RevokeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
        await harness.Revocations.Received(1).RevokeAsync(SessionIdOf(Principal), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revalidator_ActivityWithinTheLimit_StaysActive()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shared = ActiveSharedClient();
        var harness = Create(clock, shared);
        clock.Advance(IdleLimit - OneMinute);
        harness.Tracker.Touch();
        clock.Advance(IdleLimit - OneMinute);

        var valid = await harness.Sut.IsStillValidAsync(Principal, Xunit.TestContext.Current.CancellationToken);

        Assert.True(valid);
        await shared.DidNotReceive().RevokeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revalidator_JustUnderTheLimit_StaysActive()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shared = ActiveSharedClient();
        var harness = Create(clock, shared);
        clock.Advance(IdleLimit - TimeSpan.FromMilliseconds(1));

        var valid = await harness.Sut.IsStillValidAsync(Principal, Xunit.TestContext.Current.CancellationToken);

        Assert.True(valid);
        await shared.DidNotReceive().RevokeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revalidator_LocallyRevoked_DoesNotCallTheApi()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shared = ActiveSharedClient();
        var harness = Create(clock, shared, sessionActive: false);

        var valid = await harness.Sut.IsStillValidAsync(Principal, Xunit.TestContext.Current.CancellationToken);

        Assert.False(valid);
        await shared.DidNotReceive().CheckAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
        await shared.DidNotReceive().RevokeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revalidator_ApiSaysEnded_RevokesLocally()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shared = Substitute.For<ISharedSessionClient>();
        shared.CheckAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(SharedSessionState.Ended));
        var harness = Create(clock, shared);

        var valid = await harness.Sut.IsStillValidAsync(Principal, Xunit.TestContext.Current.CancellationToken);

        Assert.False(valid);
        await harness.Revocations.Received(1).RevokeAsync(SessionIdOf(Principal), Arg.Any<CancellationToken>());
        await shared.DidNotReceive().RevokeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revalidator_ApiSaysForbidden_EndsLocally()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shared = Substitute.For<ISharedSessionClient>();
        shared.CheckAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(SharedSessionState.Forbidden));
        var harness = Create(clock, shared);

        var valid = await harness.Sut.IsStillValidAsync(Principal, Xunit.TestContext.Current.CancellationToken);

        Assert.False(valid);
        await harness.Revocations.Received(1).RevokeAsync(SessionIdOf(Principal), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revalidator_ApiUnavailable_StaysActiveAndLogsAWarning()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var tokens = Substitute.For<IApiAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.Bearer("token"));
        var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.test/") };
        var shared = new ApiSharedSessionClient(http, tokens, factory.CreateLogger<ApiSharedSessionClient>());
        var harness = Create(clock, shared);

        var valid = await harness.Sut.IsStillValidAsync(Principal, Xunit.TestContext.Current.CancellationToken);

        Assert.True(valid);
        Assert.Contains("Shared session check failed (HttpRequestException)", provider.AllText, StringComparison.Ordinal);
        await harness.Revocations.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revalidator_HeartbeatsNeverExtendPastTheEightHourExpiry()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var identity = new ClaimsIdentity(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"), "test");
        SessionClaimsIssuer.Add(identity, clock);
        var principal = new ClaimsPrincipal(identity);
        var validator = new SessionValidator(Substitute.For<ISessionRevocationStore>(), clock);
        var shared = ActiveSharedClient();
        var harness = Create(clock, shared, validator);
        var minutesBeforeExpiry = (int)(SessionLength / OneMinute) - 1;

        for (var minute = 0; minute < minutesBeforeExpiry; minute++)
        {
            harness.Tracker.Touch();
            clock.Advance(OneMinute);
            Assert.True(await harness.Sut.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken));
        }

        harness.Tracker.Touch();
        clock.Advance(OneMinute);

        Assert.False(await harness.Sut.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Validator_RevocationStoreThrows_DoesNotReturnActive()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var revocations = Substitute.For<ISessionRevocationStore>();
        revocations.IsRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("The revocation store is down."));
        var identity = new ClaimsIdentity(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"), "test");
        SessionClaimsIssuer.Add(identity, clock);
        var validator = new SessionValidator(revocations, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.IsActiveAsync(new ClaimsPrincipal(identity), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revalidator_RevocationStoreThrows_DoesNotReturnActive()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var identity = new ClaimsIdentity(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"), "test");
        SessionClaimsIssuer.Add(identity, clock);
        var revocations = Substitute.For<ISessionRevocationStore>();
        revocations.IsRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("The revocation store is down."));
        var shared = ActiveSharedClient();
        var harness = Create(clock, shared, new SessionValidator(revocations, clock));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Sut.IsStillValidAsync(new ClaimsPrincipal(identity), Xunit.TestContext.Current.CancellationToken));
        await shared.DidNotReceive().CheckAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    private static string SessionIdOf(ClaimsPrincipal principal) =>
        principal.FindFirst(SessionClaims.SessionId)!.Value;

    private static ISharedSessionClient ActiveSharedClient()
    {
        var shared = Substitute.For<ISharedSessionClient>();
        shared.CheckAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(SharedSessionState.Active));
        return shared;
    }

    private static Harness Create(FakeTimeProvider clock, ISharedSessionClient shared, ISessionValidator? validator = null, bool sessionActive = true)
    {
        var sessionValidator = validator ?? Substitute.For<ISessionValidator>();
        if (validator is null)
        {
            sessionValidator.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(sessionActive));
        }

        var revocations = Substitute.For<ISessionRevocationStore>();
        var tracker = new CircuitActivityTracker(new SessionActivityMap(clock));
        tracker.Bind(Principal.FindFirst(SessionClaims.SessionId)!.Value, clock.GetUtcNow().AddHours(8));
        var sut = new SessionRevalidator(sessionValidator, shared, revocations, tracker, Options.Create(new IdleLockOptions()));
        return new Harness(sut, tracker, revocations);
    }

    private sealed record Harness(SessionRevalidator Sut, CircuitActivityTracker Tracker, ISessionRevocationStore Revocations);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("The network is down.");
    }
}
