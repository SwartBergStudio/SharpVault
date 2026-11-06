// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Idle;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class SessionIdleTests
{
    private static readonly TimeSpan Limit = IdleLockOptions.DefaultTimeout + SessionRevalidator.ServerIdleMargin;
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly string SessionId = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Idle_TabBIdleWithActivityInTabA_SessionStaysActive()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var map = new SessionActivityMap(clock);
        var shared = ActiveShared();
        var principal = Principal(clock);
        var tabA = Revalidator(clock, shared, map, out var circuitA);
        var tabB = Revalidator(clock, shared, map, out _);
        await tabA.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken);
        await tabB.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken);
        clock.Advance(Limit - OneMinute);
        circuitA.Touch();
        clock.Advance(Limit - OneMinute);

        var valid = await tabB.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken);

        Assert.True(valid);
    }

    [Fact]
    public async Task Idle_AllCircuitsIdlePastLimit_SessionEnds()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var map = new SessionActivityMap(clock);
        var principal = Principal(clock);
        var tabA = Revalidator(clock, ActiveShared(), map, out _);
        await tabA.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken);
        clock.Advance(Limit);

        var valid = await tabA.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken);

        Assert.False(valid);
    }

    [Fact]
    public async Task NewCircuit_DoesNotResetTheSessionIdleTime()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var map = new SessionActivityMap(clock);
        var shared = ActiveShared();
        var principal = Principal(clock);
        var tabA = Revalidator(clock, shared, map, out _);
        await tabA.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken);
        clock.Advance(Limit);
        var reloaded = Revalidator(clock, shared, map, out _);

        var valid = await reloaded.IsStillValidAsync(principal, Xunit.TestContext.Current.CancellationToken);

        Assert.False(valid);
    }

    [Fact]
    public void SessionActivityMap_EntryPastItsExpiry_IsRemoved()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var map = new SessionActivityMap(clock);
        map.Touch(SessionId, clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(2));

        var removed = map.RemoveExpired();

        Assert.Equal(1, removed);
        Assert.Equal(0, map.Count);
    }

    [Fact]
    public void SessionActivityMap_UnknownSession_ReportsNoActivity()
    {
        var map = new SessionActivityMap(new FakeTimeProvider(DateTimeOffset.UtcNow));

        Assert.Equal(TimeSpan.MaxValue, map.IdleFor(SessionId));
    }

    private static ISharedSessionClient ActiveShared()
    {
        var shared = Substitute.For<ISharedSessionClient>();
        shared.CheckAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(SharedSessionState.Active));
        return shared;
    }

    private static ClaimsPrincipal Principal(TimeProvider clock) => new(new ClaimsIdentity(
        [
            new Claim(SessionClaims.SessionId, SessionId),
            new Claim(SessionClaims.ExpiresAt, clock.GetUtcNow().AddHours(8).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ],
        "Test"));

    private static SessionRevalidator Revalidator(FakeTimeProvider clock, ISharedSessionClient shared, SessionActivityMap map, out CircuitActivityTracker tracker)
    {
        var validator = Substitute.For<ISessionValidator>();
        validator.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        tracker = new CircuitActivityTracker(map);
        return new SessionRevalidator(validator, shared, Substitute.For<ISessionRevocationStore>(), tracker, Options.Create(new IdleLockOptions()));
    }
}
