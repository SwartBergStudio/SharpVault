// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Idle;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class CircuitActivityTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly string SessionId = Guid.NewGuid().ToString("N");

    [Fact]
    public void CircuitActivityTracker_NoActivity_ReportsElapsedTime()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = BoundTracker(clock);

        clock.Advance(FiveMinutes);

        Assert.Equal(FiveMinutes, tracker.IdleFor);
    }

    [Fact]
    public void CircuitActivityTracker_Touch_ResetsIdleTime()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = BoundTracker(clock);
        clock.Advance(TenMinutes);

        tracker.Touch();
        clock.Advance(FiveMinutes);

        Assert.Equal(FiveMinutes, tracker.IdleFor);
    }

    [Fact]
    public void CircuitActivityTracker_FreshlyCreated_HasNoIdleTime()
    {
        var tracker = BoundTracker(new FakeTimeProvider(DateTimeOffset.UtcNow));

        Assert.Equal(TimeSpan.Zero, tracker.IdleFor);
    }

    [Fact]
    public async Task CircuitActivityHandler_InboundActivity_TouchesTheTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = BoundTracker(clock);
        var handler = Handler(tracker);
        clock.Advance(TenMinutes);
        var idleWhenNextRan = TimeSpan.MaxValue;
        var nextRan = false;

        var wrapped = handler.CreateInboundActivityHandler(_ =>
        {
            idleWhenNextRan = tracker.IdleFor;
            nextRan = true;
            return Task.CompletedTask;
        });
        await wrapped(UninitializedContext());

        Assert.True(nextRan);
        Assert.Equal(TimeSpan.Zero, idleWhenNextRan);
    }

    [Fact]
    public async Task CircuitActivityHandler_EachInboundActivity_RestartsTheIdleClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = BoundTracker(clock);
        var wrapped = Handler(tracker).CreateInboundActivityHandler(_ => Task.CompletedTask);
        clock.Advance(TenMinutes);
        await wrapped(UninitializedContext());
        clock.Advance(FiveMinutes);
        await wrapped(UninitializedContext());

        Assert.Equal(TimeSpan.Zero, tracker.IdleFor);
        clock.Advance(FiveMinutes);
        Assert.Equal(FiveMinutes, tracker.IdleFor);
    }

    [Fact]
    public async Task CircuitActivityHandler_InboundActivityThatFails_StillTouchesTheTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = BoundTracker(clock);
        var wrapped = Handler(tracker).CreateInboundActivityHandler(_ => throw new InvalidOperationException("The circuit failed."));
        clock.Advance(TenMinutes);

        await Assert.ThrowsAsync<InvalidOperationException>(() => wrapped(UninitializedContext()));

        Assert.Equal(TimeSpan.Zero, tracker.IdleFor);
    }

    private static CircuitActivityHandler Handler(CircuitActivityTracker tracker) =>
        new(
            tracker,
            Substitute.For<AuthenticationStateProvider>(),
            Substitute.For<ISharedSessionClient>(),
            Substitute.For<ISessionRevocationStore>(),
            Options.Create(new IdleLockOptions()),
            NullLogger<CircuitActivityHandler>.Instance);

    private static CircuitActivityTracker BoundTracker(FakeTimeProvider clock)
    {
        var tracker = new CircuitActivityTracker(new SessionActivityMap(clock));
        tracker.Bind(SessionId, clock.GetUtcNow().AddHours(8));
        return tracker;
    }

    private static CircuitInboundActivityContext UninitializedContext() =>
        (CircuitInboundActivityContext)RuntimeHelpers.GetUninitializedObject(typeof(CircuitInboundActivityContext));
}
