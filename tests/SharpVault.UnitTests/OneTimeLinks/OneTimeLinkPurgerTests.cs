// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Domain.Maintenance;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkPurgerTests
{
    private static readonly DateTimeOffset LeaseExpiry = OneTimeLinkSamples.Now.AddMinutes(5);
    private static readonly DateTimeOffset OldestStartHour = new(2026, 3, 25, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CurrentHour = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IOneTimeLinkStore _links = Substitute.For<IOneTimeLinkStore>();
    private readonly IMaintenanceLeaseStore _leases = Substitute.For<IMaintenanceLeaseStore>();
    private readonly FakeTimeProvider _time = new(OneTimeLinkSamples.Now);
    private readonly RecordingLogger<OneTimeLinkPurger> _logger = new();
    private readonly List<DateTimeOffset> _queriedHours = [];
    private readonly Dictionary<DateTimeOffset, List<OneTimeLinkExpiryEntry>> _buckets = [];
    private OneTimeLinkOptions _options = new();
    private DateTimeOffset? _checkpoint;
    private bool _renewalsGranted = true;
    private bool _leaseAvailable = true;

    public OneTimeLinkPurgerTests()
    {
        _leases.TryAcquireAsync(OneTimeLinkPurger.LeaseName, Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_leaseAvailable ? AcquiredLease() : null));
        _leases.RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_renewalsGranted
                ? new Versioned<MaintenanceLease>(call.Arg<MaintenanceLease>(), "renewed-etag")
                : null));
        _links.ListExpiryBucketAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var hour = call.Arg<DateTimeOffset>();
                _queriedHours.Add(hour);
                var entries = _buckets.TryGetValue(hour, out var bucket) ? bucket : [];
                return Task.FromResult(new OneTimeLinkExpiryPage(entries, null));
            });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Versioned<MaintenanceLease> AcquiredLease() => new(
        new MaintenanceLease
        {
            Name = OneTimeLinkPurger.LeaseName,
            Holder = "holder",
            ExpiresAt = LeaseExpiry,
            LastCompletedAt = _checkpoint,
        },
        "lease-etag");

    private OneTimeLinkPurger Purger() => new(
        _links,
        _leases,
        _time,
        Options.Create(_options),
        _logger);

    private static OneTimeLinkExpiryEntry ExpiryOf(DateTimeOffset expiresAt) => new()
    {
        LinkId = Guid.NewGuid(),
        CreatorId = Guid.NewGuid(),
        ExpiresAt = expiresAt,
    };

    private void GivenBucket(DateTimeOffset hour, params OneTimeLinkExpiryEntry[] entries) => _buckets[hour] = [.. entries];

    [Fact]
    public async Task Purge_NoCheckpoint_StartsAtSevenDaysAndOneHourBack()
    {
        await Purger().PurgeExpiredAsync(Token);

        Assert.Equal(OldestStartHour, _queriedHours[0]);
        Assert.Equal(CurrentHour, _queriedHours[^1]);
    }

    [Fact]
    public async Task Purge_Checkpoint_StartsAtItsHour()
    {
        _checkpoint = new DateTimeOffset(2026, 3, 31, 9, 37, 0, TimeSpan.Zero);

        await Purger().PurgeExpiredAsync(Token);

        Assert.Equal(new DateTimeOffset(2026, 3, 31, 9, 0, 0, TimeSpan.Zero), _queriedHours[0]);
        Assert.Equal(CurrentHour, _queriedHours[^1]);
    }

    [Fact]
    public async Task Purge_EntryNotYetExpired_IsKept()
    {
        var expired = ExpiryOf(CurrentHour.AddMinutes(-30));
        var kept = ExpiryOf(CurrentHour.AddMinutes(30));
        GivenBucket(CurrentHour.AddHours(-1), expired);
        GivenBucket(CurrentHour, kept);

        await Purger().PurgeExpiredAsync(Token);

        await _links.Received(1).DeleteExpiredAsync(expired, Arg.Any<CancellationToken>());
        await _links.DidNotReceive().DeleteExpiredAsync(kept, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Purge_LeaseLostMidRun_Stops()
    {
        _renewalsGranted = false;
        GivenBucket(CurrentHour.AddHours(-1), ExpiryOf(CurrentHour.AddHours(-2)));

        await Purger().PurgeExpiredAsync(Token);

        Assert.Single(_queriedHours);
        await _leases.DidNotReceive().ReleaseAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Contains(_logger.Records, record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Purge_CompletedRun_MovesTheCheckpoint()
    {
        await Purger().PurgeExpiredAsync(Token);

        await _leases.Received(1).ReleaseAsync(
            Arg.Is<MaintenanceLease>(lease => lease.LastCompletedAt == OneTimeLinkSamples.Now),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Purge_LeaseHeld_DoesNothing()
    {
        _leaseAvailable = false;

        await Purger().PurgeExpiredAsync(Token);

        Assert.Empty(_queriedHours);
        await _leases.DidNotReceive().RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Purge_FeatureDisabled_StillDeletesExpiredLinks()
    {
        _options = new OneTimeLinkOptions { Enabled = false, PurgeEnabled = false };
        var expired = ExpiryOf(CurrentHour.AddMinutes(-1));
        GivenBucket(CurrentHour.AddHours(-1), expired);

        await Purger().PurgeExpiredAsync(Token);

        await _links.Received(1).DeleteExpiredAsync(expired, Arg.Any<CancellationToken>());
    }
}
