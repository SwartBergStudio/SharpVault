// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Sessions;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TableSessionRevocationLedgerTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string PartitionDayFormat = "yyyyMMdd";
    private const int OldDaysBack = 2;
    private const int ManyRowCount = 150;
    private const int PagedRowCount = 1100;
    private const int FirstPageRowCount = 1000;
    private const int SingleRow = 1;
    private const int NothingRemoved = 0;

    private ISessionRevocationLedger Ledger => Factory.Services.GetRequiredService<ISessionRevocationLedger>();

    [Fact]
    public async Task Purge_OldPartitions_AreDeleted_TodayIsKept()
    {
        var now = DateTimeOffset.UtcNow;
        var oldPartition = PartitionFor(now.AddDays(-OldDaysBack));
        var todayPartition = PartitionFor(now);
        var oldRow = RowKey();
        var todayRow = RowKey();
        await InsertAsync(oldPartition, oldRow);
        await InsertAsync(todayPartition, todayRow);

        var removed = await Ledger.PurgeExpiredAsync(now, TestContext.Current.CancellationToken);

        Assert.Equal(SingleRow, removed);
        Assert.Empty(await RowsAsync(oldPartition));
        Assert.Equal(todayRow, Assert.Single(await RowsAsync(todayPartition)));
    }

    [Fact]
    public async Task Purge_ManyRows_ChunksTransactions()
    {
        var now = DateTimeOffset.UtcNow;
        var partition = PartitionFor(now.AddDays(-OldDaysBack));
        for (var index = 0; index < ManyRowCount; index++)
        {
            await InsertAsync(partition, RowKey());
        }

        var removed = await Ledger.PurgeExpiredAsync(now, TestContext.Current.CancellationToken);

        Assert.Equal(ManyRowCount, removed);
        Assert.Empty(await RowsAsync(partition));
    }

    [Fact]
    public async Task Purge_SeveralPages_DeletesEachPageBeforeTheNextQuery()
    {
        var now = DateTimeOffset.UtcNow;
        var partition = PartitionFor(now.AddDays(-OldDaysBack));
        var table = GetTableClient(TableNames.SessionRevocations);
        for (var batch = 0; batch < PagedRowCount / VaultStorageLimits.MaxActionsPerTransaction; batch++)
        {
            var actions = Enumerable.Range(0, VaultStorageLimits.MaxActionsPerTransaction)
                .Select(_ => new TableTransactionAction(TableTransactionActionType.Add, new TableEntity(partition, RowKey())))
                .ToList();
            await table.SubmitTransactionAsync(actions, TestContext.Current.CancellationToken);
        }

        StorageRequests.Clear();
        var removed = await Ledger.PurgeExpiredAsync(now, TestContext.Current.CancellationToken);

        var sequence = StorageRequests.Requests
            .Where(request => request.Method == "POST" || request.Filter?.Contains(partition, StringComparison.Ordinal) == true)
            .Select(request => request.Method == "POST" ? "Write" : "Query")
            .ToList();
        var firstPageWrites = Enumerable.Repeat("Write", FirstPageRowCount / VaultStorageLimits.MaxActionsPerTransaction);
        var secondPageWrites = Enumerable.Repeat("Write", (PagedRowCount - FirstPageRowCount) / VaultStorageLimits.MaxActionsPerTransaction);
        var expected = new List<string> { "Query" }.Concat(firstPageWrites).Append("Query").Concat(secondPageWrites).ToList();
        Assert.Equal(PagedRowCount, removed);
        Assert.Equal(expected, sequence);
        Assert.Empty(await RowsAsync(partition));
    }

    [Fact]
    public async Task Purge_QueriesAlwaysHaveAPartitionKey()
    {
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(PartitionFor(now.AddDays(-OldDaysBack)), RowKey());
        StorageRequests.Clear();

        await Ledger.PurgeExpiredAsync(now, TestContext.Current.CancellationToken);

        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task Purge_NothingToRemove_RemovesNothing()
    {
        var removed = await Ledger.PurgeExpiredAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(NothingRemoved, removed);
    }

    [Fact]
    public async Task Revoke_TwiceForTheSameSession_IsIdempotent()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        await Ledger.RevokeAsync(userId, sessionId, expiresAt, TestContext.Current.CancellationToken);
        await Ledger.RevokeAsync(userId, sessionId, expiresAt, TestContext.Current.CancellationToken);

        Assert.True(await Ledger.IsRevokedAsync(userId, sessionId, expiresAt, TestContext.Current.CancellationToken));
        Assert.Single(await RowsAsync(PartitionFor(expiresAt)));
    }

    [Fact]
    public async Task IsRevoked_UnknownSession_ReturnsFalse()
    {
        var revoked = await Ledger.IsRevokedAsync(Guid.NewGuid(), Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddHours(1), TestContext.Current.CancellationToken);

        Assert.False(revoked);
    }

    private static string PartitionFor(DateTimeOffset value) => value.UtcDateTime.ToString(PartitionDayFormat, CultureInfo.InvariantCulture);

    private static string RowKey() => Guid.NewGuid().ToString("N") + "_" + Guid.NewGuid().ToString("N");

    private async Task InsertAsync(string partition, string rowKey) =>
        await GetTableClient(TableNames.SessionRevocations).AddEntityAsync(new TableEntity(partition, rowKey), TestContext.Current.CancellationToken);

    private async Task<IReadOnlyList<string>> RowsAsync(string partition)
    {
        var rows = new List<string>();
        await foreach (var row in GetTableClient(TableNames.SessionRevocations).QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {partition}"), select: ["RowKey"], cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(row.RowKey);
        }

        return rows;
    }
}
