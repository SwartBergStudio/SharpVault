// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Domain.Audit;
using SharpVault.Infrastructure.Audit;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Audit;

[Collection(MapsterGlobalSettings.Name)]
public sealed class TableAuditLogTests
{
    private readonly TableClient _table = Substitute.For<TableClient>();
    private readonly TableClient _index = Substitute.For<TableClient>();
    private readonly TableAuditLog _log;

    public TableAuditLogTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.AuditLog).Returns(_table);
        service.GetTableClient(TableNames.SecretAuditLog).Returns(_index);
        _log = new TableAuditLog(service);
        TypeAdapterRegistration.EnsureInfrastructureMappings();
    }

    [Fact]
    public async Task AppendAsync_Entry_InsertsExactlyOneRowAndNothingElse()
    {
        var entry = NewEntry();

        await _log.AppendAsync(entry, TestContext.Current.CancellationToken);

        await _table.Received(1).AddEntityAsync(
            Arg.Is<AuditLogTableEntity>(row => row.RowKey == AuditKeys.RowKey(entry.OccurredAt, entry.Id) && row.EventType == "SignIn"),
            Arg.Any<CancellationToken>());
        Assert.Single(_table.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppendAsync_DuplicateKeys_FailsInsteadOfOverwriting()
    {
        _table.AddEntityAsync(Arg.Any<AuditLogTableEntity>(), Arg.Any<CancellationToken>()).ThrowsAsync(new RequestFailedException(409, "exists"));

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _log.AppendAsync(NewEntry(), TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.Status);
        await _table.DidNotReceive().UpsertEntityAsync(Arg.Any<AuditLogTableEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
        await _table.DidNotReceive().UpdateEntityAsync(Arg.Any<AuditLogTableEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendAsync_OwnerAndSecretSet_WritesBothRows()
    {
        var entry = NewEntry();
        entry.OwnerId = Guid.NewGuid();
        entry.SecretId = Guid.NewGuid();

        await _log.AppendAsync(entry, TestContext.Current.CancellationToken);

        await _table.Received(1).AddEntityAsync(
            Arg.Is<AuditLogTableEntity>(row => row.OwnerId == entry.OwnerId.ToString() && row.SecretId == entry.SecretId.ToString()),
            Arg.Any<CancellationToken>());
        await _index.Received(1).AddEntityAsync(
            Arg.Is<SecretAuditLogTableEntity>(row =>
                row.PartitionKey == entry.OwnerId.Value.ToString("D")
                && row.RowKey == SecretAuditKeys.RowKey(entry.SecretId.Value, entry.OccurredAt, entry.Id)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendAsync_NoSecretId_WritesOnlyAuditRow()
    {
        var entry = NewEntry();
        entry.OwnerId = Guid.NewGuid();

        await _log.AppendAsync(entry, TestContext.Current.CancellationToken);

        Assert.Single(_table.ReceivedCalls());
        Assert.Empty(_index.ReceivedCalls());
    }

    [Fact]
    public async Task AppendAsync_NoOwnerId_WritesOnlyAuditRow()
    {
        var entry = NewEntry();
        entry.SecretId = Guid.NewGuid();

        await _log.AppendAsync(entry, TestContext.Current.CancellationToken);

        Assert.Single(_table.ReceivedCalls());
        Assert.Empty(_index.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppendAsync_IndexWriteThrows_Propagates()
    {
        var entry = NewEntry();
        entry.OwnerId = Guid.NewGuid();
        entry.SecretId = Guid.NewGuid();
        _index.AddEntityAsync(Arg.Any<SecretAuditLogTableEntity>(), Arg.Any<CancellationToken>()).ThrowsAsync(new RequestFailedException(500, "index down"));

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() => _log.AppendAsync(entry, TestContext.Current.CancellationToken));

        Assert.Equal(500, exception.Status);
        await _table.Received(1).AddEntityAsync(Arg.Any<AuditLogTableEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppendAsync_PrimaryWriteThrows_NeverWritesTheIndex()
    {
        var entry = NewEntry();
        entry.OwnerId = Guid.NewGuid();
        entry.SecretId = Guid.NewGuid();
        _table.AddEntityAsync(Arg.Any<AuditLogTableEntity>(), Arg.Any<CancellationToken>()).ThrowsAsync(new RequestFailedException(500, "down"));

        await Assert.ThrowsAsync<RequestFailedException>(() => _log.AppendAsync(entry, TestContext.Current.CancellationToken));

        Assert.Empty(_index.ReceivedCalls());
    }

    [Fact]
    public async Task AppendAsync_NullEntry_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _log.AppendAsync(null!, TestContext.Current.CancellationToken));
    }

    private static AuditEntry NewEntry() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = AuditEventType.SignIn,
            ActorId = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
            IpAddress = "10.0.0.1",
        };
}
