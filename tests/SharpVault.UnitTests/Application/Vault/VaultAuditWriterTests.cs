// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class VaultAuditWriterTests
{
    private readonly RecordingAuditLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly StubCurrentUser _actor = new();
    private readonly VaultAuditWriter _writer;

    public VaultAuditWriterTests()
    {
        _writer = new VaultAuditWriter(_actor, new StubRequestMetadata("198.51.100.4"), _log, _time, NullLogger<VaultAuditWriter>.Instance);
    }

    [Fact]
    public async Task WriteAsync_SecretEvent_SetsActorOwnerTargetSecretTimeAndIp()
    {
        var owner = Guid.NewGuid();
        var secret = Guid.NewGuid();

        await _writer.WriteAsync(new VaultAuditItem(AuditEventType.SecretValueRevealed, owner, secret, secret), TestContext.Current.CancellationToken);

        var entry = Assert.Single(_log.Entries);
        Assert.Equal(AuditEventType.SecretValueRevealed, entry.EventType);
        Assert.Equal(_actor.UserId, entry.ActorId);
        Assert.Equal(owner, entry.OwnerId);
        Assert.Equal(secret.ToString("D"), entry.TargetId);
        Assert.Equal(secret, entry.SecretId);
        Assert.Equal(_time.GetUtcNow(), entry.OccurredAt);
        Assert.Equal("198.51.100.4", entry.IpAddress);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    [Fact]
    public async Task WriteAsync_FolderEvent_LeavesSecretIdNull()
    {
        var owner = Guid.NewGuid();
        var folder = Guid.NewGuid();

        await _writer.WriteAsync(new VaultAuditItem(AuditEventType.FolderCreated, owner, folder, null), TestContext.Current.CancellationToken);

        var entry = Assert.Single(_log.Entries);
        Assert.Null(entry.SecretId);
        Assert.Equal(folder.ToString("D"), entry.TargetId);
        Assert.Equal(owner, entry.OwnerId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WriteAsync_ReasonIsNull()
    {
        await _writer.WriteAsync(new VaultAuditItem(AuditEventType.SecretCreated, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(_log.Entries).Reason);
    }

    [Fact]
    public async Task WriteAsync_AdminActingOnAnotherOwner_RecordsTheAdminAsActorAndTheOwnerSeparately()
    {
        var admin = new StubCurrentUser(isAdmin: true);
        var writer = new VaultAuditWriter(admin, new StubRequestMetadata(null), _log, _time, NullLogger<VaultAuditWriter>.Instance);
        var owner = Guid.NewGuid();

        await writer.WriteAsync(new VaultAuditItem(AuditEventType.SecretPurged, owner, Guid.NewGuid(), null), TestContext.Current.CancellationToken);

        var entry = Assert.Single(_log.Entries);
        Assert.Equal(admin.UserId, entry.ActorId);
        Assert.Equal(owner, entry.OwnerId);
        Assert.NotEqual(entry.ActorId, entry.OwnerId);
        Assert.Null(entry.IpAddress);
    }

    [Fact]
    public async Task WriteAsync_EveryEntryGetsAFreshId()
    {
        var item = new VaultAuditItem(AuditEventType.SecretUpdated, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await _writer.WriteAsync(item, TestContext.Current.CancellationToken);
        await _writer.WriteAsync(item, TestContext.Current.CancellationToken);

        Assert.Equal(2, _log.Entries.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public async Task WriteAsync_LogThrows_Propagates()
    {
        _log.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => _writer.WriteAsync(
            new VaultAuditItem(AuditEventType.SecretValueRevealed, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteAsync_NullItem_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _writer.WriteAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteManyAsync_HundredItems_AppendsHundredEntries()
    {
        var owner = Guid.NewGuid();
        var items = Enumerable.Range(0, 100)
            .Select(_ => new VaultAuditItem(AuditEventType.SecretDeleted, owner, Guid.NewGuid(), Guid.NewGuid()))
            .ToList();

        await _writer.WriteManyAsync(items, TestContext.Current.CancellationToken);

        Assert.Equal(100, _log.Entries.Count);
        Assert.Equal(items.Select(item => item.TargetId.ToString("D")).Order(), _log.Entries.Select(entry => entry.TargetId!).Order());
        Assert.All(_log.Entries, entry => Assert.Equal(_actor.UserId, entry.ActorId));
    }

    [Fact]
    public async Task WriteManyAsync_Empty_AppendsNothing()
    {
        await _writer.WriteManyAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task WriteManyAsync_OneThrows_Propagates()
    {
        var poisoned = Guid.NewGuid();
        _log.FailWhen = entry => entry.TargetId == poisoned.ToString("D") ? new InvalidOperationException("index down") : null;
        var items = Enumerable.Range(0, 20)
            .Select(_ => new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
            .Append(new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), poisoned, Guid.NewGuid()))
            .ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.WriteManyAsync(items, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteManyAsync_CancelledToken_StopsWithoutWriting()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var items = Enumerable.Range(0, 10)
            .Select(_ => new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
            .ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _writer.WriteManyAsync(items, source.Token));

        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task WriteManyAsync_NullItems_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _writer.WriteManyAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteManyAsync_FirstItemThrows_StillAttemptsEveryOtherItem()
    {
        var poisoned = Guid.NewGuid();
        _log.FailWhen = entry => entry.TargetId == poisoned.ToString("D") ? new InvalidOperationException("index down") : null;
        var items = new[] { new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), poisoned, Guid.NewGuid()) }
            .Concat(Enumerable.Range(0, 200).Select(_ => new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())))
            .ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.WriteManyAsync(items, TestContext.Current.CancellationToken));

        Assert.Equal(200, _log.Entries.Count);
    }

    [Fact]
    public async Task WriteManyAsync_SeveralThrow_LogsEachFailureOnceAtError()
    {
        var logger = new RecordingLogger<VaultAuditWriter>();
        var writer = new VaultAuditWriter(_actor, new StubRequestMetadata(null), _log, _time, logger);
        _log.Failure = new InvalidOperationException("index down");
        var items = Enumerable.Range(0, 3)
            .Select(_ => new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
            .ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteManyAsync(items, TestContext.Current.CancellationToken));

        Assert.Equal(3, logger.Records.Count(record => record.Level == LogLevel.Error));
        Assert.DoesNotContain("index down", logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteCommittedAsync_CallerHasNoToken_WritesTheEntry()
    {
        await _writer.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Single(_log.Entries);
    }

    [Fact]
    public async Task WriteCommittedAsync_AuditCancelled_LogsErrorAndThrows()
    {
        var logger = new RecordingLogger<VaultAuditWriter>();
        var writer = new VaultAuditWriter(_actor, new StubRequestMetadata(null), _log, _time, logger);
        _log.Failure = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteCommittedAsync(
            new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));

        var record = Assert.Single(logger.Records, entry => entry.Level == LogLevel.Error);
        Assert.Contains(nameof(OperationCanceledException), record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteCommittedAsync_AuditHangs_TimesOutLogsAndThrowsTimeout()
    {
        var logger = new RecordingLogger<VaultAuditWriter>();
        var hanging = new HangingAuditLog();
        var writer = new VaultAuditWriter(_actor, new StubRequestMetadata(null), hanging, _time, logger);

        var pending = writer.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretDeleted, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        await hanging.Started;
        _time.Advance(VaultLimits.CommittedAuditTimeout + TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<TimeoutException>(() => pending);
        Assert.Single(logger.Records, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task WriteAsync_RequestAborted_IsNotLoggedAsAnAuditFailure()
    {
        var logger = new RecordingLogger<VaultAuditWriter>();
        var writer = new VaultAuditWriter(_actor, new StubRequestMetadata(null), _log, _time, logger);
        using var abort = new CancellationTokenSource();
        await abort.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(
            new VaultAuditItem(AuditEventType.SecretValueRevealed, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), abort.Token));

        Assert.Empty(logger.Records);
    }
}
