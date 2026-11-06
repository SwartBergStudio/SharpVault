// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.IntegrationTests.Vault;

public static class VaultRows
{
    private const string DeletedAtColumn = "DeletedAt";

    public static async Task<TableEntity?> GetAsync(TableClient table, Guid owner, string rowKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);

        var response = await table.GetEntityIfExistsAsync<TableEntity>(VaultKeys.PartitionKey(owner), rowKey, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    public static async Task<IReadOnlyList<TableEntity>> ListPartitionAsync(TableClient table, Guid owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);

        var partition = VaultKeys.PartitionKey(owner);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        var rows = new List<TableEntity>();
        await foreach (var row in table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
        {
            rows.Add(row);
        }

        return rows;
    }

    public static async Task CopyPropertyAsync(TableClient table, Guid owner, string fromRowKey, string toRowKey, string property, CancellationToken cancellationToken)
    {
        var from = await RequireAsync(table, owner, fromRowKey, cancellationToken);
        var to = await RequireAsync(table, owner, toRowKey, cancellationToken);
        to[property] = from[property];
        await table.UpdateEntityAsync(to, ETag.All, TableUpdateMode.Replace, cancellationToken);
    }

    public static async Task SwapPropertiesAsync(TableClient table, Guid owner, string rowKey, string first, string second, CancellationToken cancellationToken)
    {
        var row = await RequireAsync(table, owner, rowKey, cancellationToken);
        (row[first], row[second]) = (row[second], row[first]);
        await table.UpdateEntityAsync(row, ETag.All, TableUpdateMode.Replace, cancellationToken);
    }

    public static async Task CopyRowToPartitionAsync(TableClient table, Guid fromOwner, string rowKey, Guid toOwner, CancellationToken cancellationToken)
    {
        var row = await RequireAsync(table, fromOwner, rowKey, cancellationToken);
        var copy = new TableEntity(VaultKeys.PartitionKey(toOwner), rowKey);
        foreach (var property in row)
        {
            if (property.Key is not ("PartitionKey" or "RowKey" or "Timestamp" or "odata.etag"))
            {
                copy[property.Key] = property.Value;
            }
        }

        await table.UpsertEntityAsync(copy, TableUpdateMode.Replace, cancellationToken);
    }

    public static async Task SetPropertyAsync(TableClient table, Guid owner, string rowKey, string property, object value, CancellationToken cancellationToken)
    {
        var row = await RequireAsync(table, owner, rowKey, cancellationToken);
        row[property] = value;
        await table.UpdateEntityAsync(row, ETag.All, TableUpdateMode.Replace, cancellationToken);
    }

    public static async Task SetDeletedAtAsync(TableClient table, Guid owner, string rowKey, DateTimeOffset deletedAt, CancellationToken cancellationToken)
    {
        var row = await RequireAsync(table, owner, rowKey, cancellationToken);
        row[DeletedAtColumn] = deletedAt;
        await table.UpdateEntityAsync(row, ETag.All, TableUpdateMode.Replace, cancellationToken);
    }

    private static async Task<TableEntity> RequireAsync(TableClient table, Guid owner, string rowKey, CancellationToken cancellationToken) =>
        await GetAsync(table, owner, rowKey, cancellationToken)
        ?? throw new InvalidOperationException($"The row {rowKey} does not exist.");
}
