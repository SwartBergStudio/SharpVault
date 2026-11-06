// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Azure;
using Azure.Data.Tables;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Users;

public sealed class TableSessionRevocationLedger(TableServiceClient tableServiceClient) : ISessionRevocationLedger
{
    private const int ConflictStatus = 409;
    private const int NotFoundStatus = 404;
    private const int PurgeWindowDays = 30;
    private const string PartitionDayFormat = "yyyyMMdd";
    private const string RowKeyColumn = "RowKey";
    private const string RowKeyGuidFormat = "N";

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.SessionRevocations);

    public async Task RevokeAsync(Guid userId, string sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var row = new TableEntity(PartitionOf(expiresAt), RowKeyOf(userId, sessionId));
        try
        {
            await _table.AddEntityAsync(row, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == ConflictStatus)
        {
        }
    }

    public async Task<bool> IsRevokedAsync(Guid userId, string sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            PartitionOf(expiresAt),
            RowKeyOf(userId, sessionId),
            select: [RowKeyColumn],
            cancellationToken: cancellationToken);
        return response.HasValue;
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var removed = 0;
        var today = now.UtcDateTime.Date;
        for (var daysBack = 1; daysBack <= PurgeWindowDays; daysBack++)
        {
            var partition = today.AddDays(-daysBack).ToString(PartitionDayFormat, CultureInfo.InvariantCulture);
            removed += await PurgePartitionAsync(partition, cancellationToken);
        }

        return removed;
    }

    private async Task<int> PurgePartitionAsync(string partition, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        var removed = 0;
        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, select: [RowKeyColumn], cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            foreach (var chunk in page.Values.Chunk(VaultStorageLimits.MaxActionsPerTransaction))
            {
                removed += await DeleteChunkAsync(partition, chunk, cancellationToken);
            }
        }

        return removed;
    }

    private async Task<int> DeleteChunkAsync(string partition, IEnumerable<TableEntity> chunk, CancellationToken cancellationToken)
    {
        var actions = chunk
            .Select(row => new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(partition, row.RowKey), row.ETag))
            .ToList();
        try
        {
            await _table.SubmitTransactionAsync(actions, cancellationToken);
            return actions.Count;
        }
        catch (TableTransactionFailedException exception) when (exception.Status == NotFoundStatus)
        {
            return 0;
        }
    }

    private static string PartitionOf(DateTimeOffset expiresAt) =>
        expiresAt.UtcDateTime.ToString(PartitionDayFormat, CultureInfo.InvariantCulture);

    private static string RowKeyOf(Guid userId, string sessionId) =>
        $"{userId.ToString(RowKeyGuidFormat, CultureInfo.InvariantCulture)}_{sessionId}";
}
