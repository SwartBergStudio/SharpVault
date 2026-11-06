// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Maintenance;

namespace SharpVault.UnitTests.Application.Transfers.Job;

public sealed class TransferLeaseKeeperTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Third = Duration / 3;

    private readonly IMaintenanceLeaseStore _leases = Substitute.For<IMaintenanceLeaseStore>();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly Versioned<MaintenanceLease> _lease;
    private readonly TransferLeaseKeeper _keeper;

    public TransferLeaseKeeperTests()
    {
        _lease = new Versioned<MaintenanceLease>(
            new MaintenanceLease { Name = "transfers", Holder = "node-a", ExpiresAt = Start.Add(Duration) },
            "etag-1");
        _keeper = new TransferLeaseKeeper(_leases, _time, Duration, _lease);
    }

    private void RenewalReturns(string etag) =>
        _leases.RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<MaintenanceLease>?>(new Versioned<MaintenanceLease>(_lease.Value, etag)));

    [Fact]
    public async Task RenewIfDue_BeforeAThirdOfTheDuration_DoesNotRenew()
    {
        _time.Advance(Third - TimeSpan.FromMilliseconds(1));

        await _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken);

        _ = _leases.DidNotReceiveWithAnyArgs().RenewAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RenewIfDue_AfterAThirdOfTheDuration_Renews()
    {
        RenewalReturns("etag-2");
        _time.Advance(Third);

        await _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken);

        _ = _leases.Received(1).RenewAsync(_lease.Value, "etag-1", Duration, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RenewIfDue_AfterARenewal_WaitsAnotherThirdBeforeRenewingAgain()
    {
        RenewalReturns("etag-2");
        _time.Advance(Third);
        await _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken);

        _time.Advance(Third - TimeSpan.FromMilliseconds(1));
        await _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken);

        _ = _leases.Received(1).RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenewIfDue_SecondRenewal_UsesTheETagReturnedByTheFirst()
    {
        RenewalReturns("etag-2");
        _time.Advance(Third);
        await _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken);

        _time.Advance(Third);
        await _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken);

        _ = _leases.Received(1).RenewAsync(_lease.Value, "etag-2", Duration, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RenewIfDue_RenewalReturnsNothing_ThrowsLeaseLost()
    {
        _leases.RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<MaintenanceLease>?>(null));
        _time.Advance(Third);

        await Assert.ThrowsAsync<TransferLeaseLostException>(() => _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenewIfDue_LeaseAlreadyExpired_ThrowsLeaseLostWithoutRenewing()
    {
        _time.Advance(Duration);

        await Assert.ThrowsAsync<TransferLeaseLostException>(() => _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken));

        _ = _leases.DidNotReceiveWithAnyArgs().RenewAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReleaseAsync_AfterARenewal_ReleasesWithTheLatestETag()
    {
        RenewalReturns("etag-2");
        _time.Advance(Third);
        await _keeper.RenewIfDueAsync(TestContext.Current.CancellationToken);

        await _keeper.ReleaseAsync(TestContext.Current.CancellationToken);

        _ = _leases.Received(1).ReleaseAsync(_lease.Value, "etag-2", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReleaseAsync_BeforeAnyRenewal_ReleasesWithTheAcquiredETag()
    {
        await _keeper.ReleaseAsync(TestContext.Current.CancellationToken);

        _ = _leases.Received(1).ReleaseAsync(_lease.Value, "etag-1", TestContext.Current.CancellationToken);
    }
}
