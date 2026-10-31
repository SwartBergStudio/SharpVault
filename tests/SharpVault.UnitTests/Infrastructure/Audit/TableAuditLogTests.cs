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
    private readonly TableAuditLog _log;

    public TableAuditLogTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.AuditLog).Returns(_table);
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
