// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Audit;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Users;

[Trait("Category", "Security")]
public sealed class AdminAuditWriterTests
{
    private const string Reason = "Left the company and handed over everything";

    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<AdminAuditWriter> _logger = new();
    private readonly AdminAuditWriter _writer;

    public AdminAuditWriterTests() => _writer = new AdminAuditWriter(_audit, _time, _logger);

    [Fact]
    public async Task WriteAsync_MapsEveryField()
    {
        AuditEntry? appended = null;
        await _audit.AppendAsync(Arg.Do<AuditEntry>(entry => appended = entry), Arg.Any<CancellationToken>());
        var actor = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var target = Guid.NewGuid();
        var transfer = Guid.NewGuid();

        await _writer.WriteAsync(
            new AdminAuditItem(AuditEventType.TransferRequested, actor, owner, target, transfer, "LeftCompany", Reason, "10.0.0.1"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(appended);
        Assert.Equal(AuditEventType.TransferRequested, appended.EventType);
        Assert.Equal(actor, appended.ActorId);
        Assert.Equal(owner, appended.OwnerId);
        Assert.Equal(target.ToString("D"), appended.TargetId);
        Assert.Equal(transfer, appended.TransferId);
        Assert.Equal("LeftCompany", appended.ReasonCategory);
        Assert.Equal(Reason, appended.Reason);
        Assert.Equal("10.0.0.1", appended.IpAddress);
        Assert.Equal(_time.GetUtcNow(), appended.OccurredAt);
        Assert.NotEqual(Guid.Empty, appended.Id);
    }

    [Fact]
    public async Task WriteAsync_NoTargetAndInvalidIp_StoresNulls()
    {
        AuditEntry? appended = null;
        await _audit.AppendAsync(Arg.Do<AuditEntry>(entry => appended = entry), Arg.Any<CancellationToken>());

        await _writer.WriteAsync(
            new AdminAuditItem(AuditEventType.TransferStarted, AuditActors.System, null, null, Guid.NewGuid(), null, null, "not an ip"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(appended);
        Assert.Null(appended.TargetId);
        Assert.Null(appended.IpAddress);
    }

    [Fact]
    public async Task WriteAsync_StoreFails_LogsTheTypeButNeverTheReason()
    {
        _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException(Reason));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.WriteAsync(
            new AdminAuditItem(AuditEventType.UserLocked, Guid.NewGuid(), null, Guid.NewGuid(), null, "Other", Reason, null),
            TestContext.Current.CancellationToken));

        Assert.NotEmpty(_logger.Records);
        Assert.DoesNotContain(Reason, _logger.AllText(), StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_Cancelled_PropagatesWithoutALog()
    {
        _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _writer.WriteAsync(
            new AdminAuditItem(AuditEventType.UserLocked, Guid.NewGuid(), null, null, null, null, null, null),
            TestContext.Current.CancellationToken));

        Assert.Empty(_logger.Records);
    }

    [Fact]
    public async Task WriteCommittedAsync_AppendsOnce()
    {
        await _writer.WriteCommittedAsync(new AdminAuditItem(AuditEventType.UserUnlocked, Guid.NewGuid(), null, Guid.NewGuid(), null, "Other", Reason, null));

        await _audit.Received(1).AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteCommittedAsync_StoreHangs_TimesOutAfterThirtySeconds()
    {
        _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()));

        var pending = _writer.WriteCommittedAsync(new AdminAuditItem(AuditEventType.UserLocked, Guid.NewGuid(), null, null, null, null, null, null));
        _time.Advance(TimeSpan.FromSeconds(31));

        await Assert.ThrowsAsync<TimeoutException>(() => pending);
    }
}
