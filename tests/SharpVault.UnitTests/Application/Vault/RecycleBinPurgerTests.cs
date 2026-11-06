// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Maintenance;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RecycleBinPurgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IUserStore _users = Substitute.For<IUserStore>();
    private readonly IVaultStore _vaults = Substitute.For<IVaultStore>();
    private readonly IShareInboxStore _inbox = Substitute.For<IShareInboxStore>();
    private readonly GrantCascadeWorld _world = new();
    private readonly IMaintenanceLeaseStore _leases = Substitute.For<IMaintenanceLeaseStore>();
    private readonly RecordingAuditLog _audit = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly List<(Guid? RunId, Guid? AfterUserId, string ETag)> _renewals = [];
    private readonly List<(Guid? RunId, Guid? AfterUserId, DateTimeOffset? Completed, string ETag)> _releases = [];
    private readonly List<IReadOnlyList<VaultChange>> _submitted = [];
    private readonly List<DateTimeOffset> _cutoffs = [];
    private readonly Dictionary<Guid, RecycledBatch> _expired = [];
    private readonly List<string> _holders = [];
    private int _etagCounter;

    public RecycleBinPurgerTests()
    {
        _leases.RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var lease = call.Arg<MaintenanceLease>();
                var etag = "renew-" + Interlocked.Increment(ref _etagCounter);
                _renewals.Add((lease.RunId, lease.AfterUserId, call.Arg<string>()));
                return Task.FromResult<Versioned<MaintenanceLease>?>(new Versioned<MaintenanceLease>(lease, etag));
            });
        _leases.ReleaseAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var lease = call.Arg<MaintenanceLease>();
                _releases.Add((lease.RunId, lease.AfterUserId, lease.LastCompletedAt, call.Arg<string>()));
                return Task.CompletedTask;
            });
        _vaults.ListRecycledBeforeAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _cutoffs.Add(call.Arg<DateTimeOffset>());
                return Task.FromResult(_expired.TryGetValue(call.Arg<Guid>(), out var batch) ? batch : new RecycledBatch([], []));
            });
        _vaults.ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<SecretVersion>>>([]));
        _vaults.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                _submitted.Add(changes);
                return Task.FromResult(new VaultWriteResult(StoreWriteResult.Written, changes.Count, [.. changes.Select(_ => (string?)"w")]));
            });
    }

    private RecycleBinPurger Purger(TimeSpan? leaseDuration = null, ILogger<RecycleBinPurger>? logger = null) => new(
        _users,
        _vaults,
        _leases,
        EraserFactory.For(_vaults, _inbox),
        _audit,
        _time,
        Options.Create(new RecycleBinPurgerOptions { LeaseDuration = leaseDuration ?? TimeSpan.FromMinutes(5) }),
        logger ?? NullLogger<RecycleBinPurger>.Instance);

    private void GivenLease(MaintenanceLease? lease = null, string etag = "acquired")
    {
        var held = lease ?? new MaintenanceLease { Name = RecycleBinPurger.LeaseName };
        _leases.TryAcquireAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _holders.Add(call.ArgAt<string>(1));
                return Task.FromResult<Versioned<MaintenanceLease>?>(new Versioned<MaintenanceLease>(held, etag));
            });
    }

    private void GivenNoLease() =>
        _leases.TryAcquireAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<MaintenanceLease>?>(null));

    private static User UserOf(Guid id) => new() { Id = id };

    private void GivenUserPages(params (Guid[] Users, Guid? Next)[] pages)
    {
        var index = 0;
        _users.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var page = pages[Math.Min(index++, pages.Length - 1)];
                return Task.FromResult(new UserListPage([.. page.Users.Select(UserOf)], page.Next));
            });
    }

    private void GivenExpired(Guid owner, IReadOnlyList<Secret> secrets, IReadOnlyList<Folder> folders) =>
        _expired[owner] = new RecycledBatch(
            [.. secrets.Select(secret => new Versioned<Secret>(secret, "s-" + secret.Id))],
            [.. folders.Select(folder => new Versioned<Folder>(folder, "f-" + folder.Id))]);

    private static Secret ExpiredSecret(Guid owner) => VaultSamples.Recycled(owner);

    private static Folder ExpiredFolder(Guid owner) => VaultSamples.RecycledFolder(owner);

    [Fact]
    public void LeaseName_TiesTheApplicationAndInfrastructureConstants()
    {
        Assert.Equal(MaintenanceKeys.RecycleBinPurgeLease, RecycleBinPurger.LeaseName);
    }

    [Fact]
    public async Task PurgeExpiredAsync_AcquiresTheLeaseUnderTheInfrastructureLeaseName()
    {
        GivenLease();
        GivenUserPages(([], null));

        await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        await _leases.Received(1).TryAcquireAsync(MaintenanceKeys.RecycleBinPurgeLease, Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgeExpiredAsync_LeaseNotAcquired_ReturnsSkippedWithoutUserScan()
    {
        GivenNoLease();
        var owner = Guid.NewGuid();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [ExpiredSecret(owner)], []);

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Skipped);
        Assert.Equal(RecycleBinPurgeResult.NotRun, result);
        await _users.DidNotReceive().ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _vaults.DidNotReceive().ListRecycledBeforeAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _vaults.DidNotReceive().SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task PurgeExpiredAsync_NewRun_ScansAllUsersAndCompletes()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var lease = new MaintenanceLease { Name = RecycleBinPurger.LeaseName };
        GivenLease(lease, "acquired");
        GivenUserPages(([first, second], second), ([third], null));
        GivenExpired(first, [ExpiredSecret(first)], [ExpiredFolder(first)]);
        GivenExpired(third, [ExpiredSecret(third), ExpiredSecret(third)], []);

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Skipped);
        Assert.Equal(3, result.UsersScanned);
        Assert.Equal(3, result.SecretsPurged);
        Assert.Equal(1, result.FoldersPurged);
        Assert.Equal(0, result.Conflicts);
        Assert.NotNull(_renewals[0].RunId);
        Assert.Null(_renewals[0].AfterUserId);
        Assert.Equal("acquired", _renewals[0].ETag);
        Assert.Equal(second, _renewals[1].AfterUserId);
        Assert.Equal(_renewals[0].RunId, _renewals[1].RunId);
        Assert.Equal("renew-2", Assert.Single(_releases).ETag);
        var release = Assert.Single(_releases);
        Assert.Null(release.RunId);
        Assert.Null(release.AfterUserId);
        Assert.Equal(Now, release.Completed);
        Assert.Equal(Now, lease.RunStartedAt);
    }

    [Fact]
    public async Task PurgeExpiredAsync_ExistingCheckpoint_ResumesAfterIt()
    {
        var checkpoint = Guid.NewGuid();
        var runId = Guid.NewGuid();
        GivenLease(new MaintenanceLease { Name = RecycleBinPurger.LeaseName, RunId = runId, AfterUserId = checkpoint, RunStartedAt = Now.AddMinutes(-10) });
        GivenUserPages(([Guid.NewGuid()], null));

        await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        await _users.Received(1).ListAsync(Arg.Any<int>(), checkpoint, Arg.Any<CancellationToken>());
        Assert.Empty(_renewals);
        Assert.Single(_releases);
    }

    [Fact]
    public async Task PurgeExpiredAsync_ExistingCheckpoint_KeepsTheRunIdAcrossRenewals()
    {
        var runId = Guid.NewGuid();
        var checkpoint = Guid.NewGuid();
        var next = Guid.NewGuid();
        GivenLease(new MaintenanceLease { Name = RecycleBinPurger.LeaseName, RunId = runId, AfterUserId = checkpoint });
        GivenUserPages(([Guid.NewGuid()], next), ([Guid.NewGuid()], null));

        await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        var renewal = Assert.Single(_renewals);
        Assert.Equal(runId, renewal.RunId);
        Assert.Equal(next, renewal.AfterUserId);
    }

    [Fact]
    public async Task PurgeExpiredAsync_FirstRenewalFails_ReturnsNotRunWithoutScan()
    {
        GivenLease();
        _leases.RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<MaintenanceLease>?>(null));

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Skipped);
        await _users.DidNotReceive().ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgeExpiredAsync_CutoffIsNowMinus30Days()
    {
        var owner = Guid.NewGuid();
        GivenLease();
        GivenUserPages(([owner], null));

        await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now - TimeSpan.FromDays(30), Assert.Single(_cutoffs));
    }

    [Fact]
    public async Task PurgeExpiredAsync_CutoffFollowsTheClock()
    {
        GivenLease();
        GivenUserPages(([Guid.NewGuid()], null));
        _time.Advance(TimeSpan.FromDays(3));

        await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddDays(3) - TimeSpan.FromDays(30), Assert.Single(_cutoffs));
    }

    [Fact]
    public async Task PurgeExpiredAsync_ItemConflict_CountsAndContinues()
    {
        var owner = Guid.NewGuid();
        var secrets = new[] { ExpiredSecret(owner), ExpiredSecret(owner), ExpiredSecret(owner) };
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, secrets, [ExpiredFolder(owner)]);
        var calls = 0;
        _vaults.SubmitAsync(owner, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                _submitted.Add(changes);
                var conflict = ++calls == 2;
                return Task.FromResult(new VaultWriteResult(conflict ? StoreWriteResult.Conflict : StoreWriteResult.Written, 0, []));
            });

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Conflicts);
        Assert.Equal(2, result.SecretsPurged);
        Assert.Equal(1, result.FoldersPurged);
        Assert.Equal(3, _audit.Entries.Count);
        Assert.DoesNotContain(_audit.Entries, entry => entry.SecretId == secrets[1].Id);
        Assert.Single(_releases);
    }

    [Fact]
    public async Task PurgeExpiredAsync_FolderConflict_CountsAndContinues()
    {
        var owner = Guid.NewGuid();
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [], [ExpiredFolder(owner), ExpiredFolder(owner)]);
        var calls = 0;
        _vaults.SubmitAsync(owner, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new VaultWriteResult(++calls == 1 ? StoreWriteResult.Conflict : StoreWriteResult.Written, 0, [])));

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Conflicts);
        Assert.Equal(1, result.FoldersPurged);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgeExpiredAsync_LeaseLostDuringRun_Stops()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        GivenLease();
        GivenUserPages(([first], first), ([second], null));
        GivenExpired(first, [ExpiredSecret(first)], []);
        GivenExpired(second, [ExpiredSecret(second)], []);
        var renewals = 0;
        _leases.RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Versioned<MaintenanceLease>?>(++renewals == 1 ? new Versioned<MaintenanceLease>(call.Arg<MaintenanceLease>(), "started") : null));

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Skipped);
        Assert.Equal(1, result.UsersScanned);
        Assert.Equal(1, result.SecretsPurged);
        await _users.Received(1).ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _leases.DidNotReceive().ReleaseAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _vaults.DidNotReceive().ListRecycledBeforeAsync(second, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgeExpiredAsync_AuditsWithSystemActorAndNoIp()
    {
        var owner = Guid.NewGuid();
        var secret = ExpiredSecret(owner);
        var folder = ExpiredFolder(owner);
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [secret], [folder]);

        await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _audit.Entries.Count);
        Assert.All(_audit.Entries, entry =>
        {
            Assert.Equal(AuditActors.System, entry.ActorId);
            Assert.Null(entry.IpAddress);
            Assert.Null(entry.Reason);
            Assert.Equal(owner, entry.OwnerId);
            Assert.Equal(Now, entry.OccurredAt);
        });
        var secretEntry = Assert.Single(_audit.Entries, entry => entry.EventType == AuditEventType.SecretPurged);
        Assert.Equal(secret.Id, secretEntry.SecretId);
        Assert.Equal(secret.Id.ToString("D"), secretEntry.TargetId);
        var folderEntry = Assert.Single(_audit.Entries, entry => entry.EventType == AuditEventType.FolderPurged);
        Assert.Null(folderEntry.SecretId);
        Assert.Equal(folder.Id.ToString("D"), folderEntry.TargetId);
    }

    [Fact]
    public async Task PurgeExpiredAsync_Cancelled_KeepsCheckpoint()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        GivenLease();
        GivenUserPages(([first], first), ([second], null));
        _vaults.ListRecycledBeforeAsync(second, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<RecycledBatch>(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken));

        Assert.Equal(first, _renewals[^1].AfterUserId);
        Assert.NotNull(_renewals[^1].RunId);
        Assert.Empty(_releases);
    }

    [Fact]
    public async Task PurgeExpiredAsync_AuditThrows_PropagatesAndKeepsTheLeaseUnreleased()
    {
        var owner = Guid.NewGuid();
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [ExpiredSecret(owner)], []);
        _audit.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken));

        Assert.Empty(_releases);
    }

    [Fact]
    public async Task PurgeExpiredAsync_AuditThrowsForSecret_LogsErrorNamingEventOwnerAndTargetOnly()
    {
        var owner = Guid.NewGuid();
        var secret = ExpiredSecret(owner);
        var logger = new RecordingLogger<RecycleBinPurger>();
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [secret], []);
        _audit.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Purger(logger: logger).PurgeExpiredAsync(TestContext.Current.CancellationToken));

        var record = Assert.Single(logger.Records, entry => entry.Level == LogLevel.Error);
        Assert.Contains(nameof(AuditEventType.SecretPurged), record.Message, StringComparison.Ordinal);
        Assert.Contains(owner.ToString("D"), record.Message, StringComparison.Ordinal);
        Assert.Contains(secret.Id.ToString("D"), record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("audit down", logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PurgeExpiredAsync_PurgesSecretVersionsWithTheSecretAndFoldersOneAtATime()
    {
        var owner = Guid.NewGuid();
        var secret = ExpiredSecret(owner);
        var version = VaultSamples.Version(owner, secret.Id);
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [secret], [ExpiredFolder(owner), ExpiredFolder(owner)]);
        _vaults.ListVersionsAsync(owner, secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<SecretVersion>>>([new Versioned<SecretVersion>(version, "v-etag")]));

        await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, _submitted.Count);
        Assert.Equal([VaultChangeKind.DeleteVersion, VaultChangeKind.PurgeSecret], _submitted[0].Select(change => change.Kind));
        Assert.Equal("s-" + secret.Id, _submitted[0][^1].ETag);
        Assert.All(_submitted.Skip(1), changes => Assert.Equal(VaultChangeKind.PurgeFolder, Assert.Single(changes).Kind));
    }

    [Fact]
    public async Task PurgeExpiredAsync_OnlyTouchesTheOwnersOfExpiredItems()
    {
        var owner = Guid.NewGuid();
        var bystander = Guid.NewGuid();
        GivenLease();
        GivenUserPages(([owner, bystander], null));
        GivenExpired(owner, [ExpiredSecret(owner)], []);

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.UsersScanned);
        await _vaults.Received(1).SubmitAsync(owner, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
        await _vaults.DidNotReceive().SubmitAsync(bystander, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PurgeExpiredAsync_UsesTheConfiguredLeaseDurationEverywhere()
    {
        var duration = TimeSpan.FromMinutes(7);
        GivenLease();
        GivenUserPages(([Guid.NewGuid()], Guid.NewGuid()), ([Guid.NewGuid()], null));

        await Purger(duration).PurgeExpiredAsync(TestContext.Current.CancellationToken);

        await _leases.Received(1).TryAcquireAsync(Arg.Any<string>(), Arg.Any<string>(), duration, Arg.Any<CancellationToken>());
        await _leases.Received(2).RenewAsync(Arg.Any<MaintenanceLease>(), Arg.Any<string>(), duration, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Options_Default_LeaseDurationIsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new RecycleBinPurgerOptions().LeaseDuration);
    }

    [Fact]
    public async Task PurgeExpiredAsync_HolderIsStablePerInstanceAndDiffersBetweenInstances()
    {
        GivenLease();
        GivenUserPages(([], null));
        var one = Purger();
        var two = Purger();

        await one.PurgeExpiredAsync(TestContext.Current.CancellationToken);
        await one.PurgeExpiredAsync(TestContext.Current.CancellationToken);
        await two.PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_holders[0], _holders[1]);
        Assert.NotEqual(_holders[0], _holders[2]);
        Assert.True(Guid.TryParse(_holders[0], out _));
    }

    [Fact]
    public async Task PurgeExpiredAsync_UserPagesAreReadWithTheCheckpointOfThePreviousPage()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        GivenLease();
        GivenUserPages(([Guid.NewGuid()], first), ([Guid.NewGuid()], second), ([Guid.NewGuid()], null));

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, result.UsersScanned);
        Received.InOrder(() =>
        {
            _users.ListAsync(100, null, Arg.Any<CancellationToken>());
            _users.ListAsync(100, first, Arg.Any<CancellationToken>());
            _users.ListAsync(100, second, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task PurgeExpiredAsync_ShutdownAfterErase_StillWritesTheAudit()
    {
        var owner = Guid.NewGuid();
        var secret = ExpiredSecret(owner);
        using var shutdown = new CancellationTokenSource();
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [secret], []);
        _vaults.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                _submitted.Add(changes);
                shutdown.Cancel();
                return Task.FromResult(new VaultWriteResult(StoreWriteResult.Written, changes.Count, [.. changes.Select(_ => (string?)"w")]));
            });

        await Purger().PurgeExpiredAsync(shutdown.Token);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditEventType.SecretPurged, entry.EventType);
        Assert.Equal(secret.Id.ToString("D"), entry.TargetId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgeExpiredAsync_SecretWithManyGrantsSecondChunkFails_AuditsCommittedGrantsSurfacesFailureAndRepeatFinishes()
    {
        var owner = Guid.NewGuid();
        var secret = ExpiredSecret(owner);
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [secret], []);
        _world.Wire(_vaults, _inbox);
        _world.AddGrants(owner, ShareItemKind.Secret, secret.Id, 150);
        _world.FailChunkNumber = 2;
        _world.FailWith = new TimeoutException("storage went away");

        await Assert.ThrowsAsync<TimeoutException>(() => Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken));

        var firstRun = _audit.Entries.ToList();
        Assert.Equal(GrantCascadeWorld.ChunkSize, firstRun.Count(entry => entry.EventType == AuditEventType.ShareErased));
        Assert.DoesNotContain(firstRun, entry => entry.EventType == AuditEventType.SecretPurged);
        Assert.All(firstRun, entry => Assert.Equal(AuditActors.System, entry.ActorId));
        Assert.False(_world.IsErased(secret.Id));
        Assert.Equal(50, _world.Grants.Count);
        Assert.Equal(50, _world.InboxRows);

        _world.ResetFailure();
        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SecretsPurged);
        Assert.Equal(50, result.GrantsErased);
        Assert.True(_world.IsErased(secret.Id));
        Assert.Empty(_world.Grants);
        Assert.Equal(0, _world.InboxRows);
        var erased = _audit.Entries.Where(entry => entry.EventType == AuditEventType.ShareErased).ToList();
        Assert.Equal(150, erased.Count);
        Assert.Equal(150, erased.Select(entry => entry.RecipientId).Distinct().Count());
        Assert.Single(_audit.Entries, entry => entry.EventType == AuditEventType.SecretPurged);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgeExpiredAsync_SecondChunkConflicts_CountsConflictAndAuditsCommittedGrants()
    {
        var owner = Guid.NewGuid();
        var secret = ExpiredSecret(owner);
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [secret], []);
        _world.Wire(_vaults, _inbox);
        _world.AddGrants(owner, ShareItemKind.Secret, secret.Id, 150);
        _world.FailChunkNumber = 2;

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Conflicts);
        Assert.Equal(0, result.SecretsPurged);
        Assert.Equal(GrantCascadeWorld.ChunkSize, result.GrantsErased);
        Assert.Equal(GrantCascadeWorld.ChunkSize, _audit.Entries.Count(entry => entry.EventType == AuditEventType.ShareErased));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgeExpiredAsync_FolderWithGrants_ErasesAndAuditsThemBeforeTheFolder()
    {
        var owner = Guid.NewGuid();
        var folder = ExpiredFolder(owner);
        GivenLease();
        GivenUserPages(([owner], null));
        GivenExpired(owner, [], [folder]);
        _world.Wire(_vaults, _inbox);
        _world.AddGrants(owner, ShareItemKind.Folder, folder.Id, 3);

        var result = await Purger().PurgeExpiredAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FoldersPurged);
        Assert.Equal(3, result.GrantsErased);
        Assert.Empty(_world.Grants);
        Assert.Equal(0, _world.InboxRows);
        Assert.Equal(3, _audit.Entries.Count(entry => entry.EventType == AuditEventType.ShareErased));
        Assert.Single(_audit.Entries, entry => entry.EventType == AuditEventType.FolderPurged);
    }
}
