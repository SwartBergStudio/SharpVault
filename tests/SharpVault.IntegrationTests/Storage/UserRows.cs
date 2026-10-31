// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Storage;

public static class UserRows
{
    public const string PartitionKey = "user";
    public const string ActiveStatus = "Active";
    public const string LockedStatus = "Locked";

    private const int TransactionBatchSize = 100;

    public static Task InsertAsync(TableClient table, TestUser user, string status, DateTimeOffset lastSignInAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(user);

        return table.AddEntityAsync(Build(user.ObjectId, user.DisplayName, user.Email, status, lastSignInAt), cancellationToken);
    }

    public static async Task InsertManyAsync(TableClient table, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);

        var now = DateTimeOffset.UtcNow;
        var entities = Enumerable.Range(0, count)
            .Select(index => Build(Guid.NewGuid(), $"Seeded user {index}", $"seeded{index}@sharpvault.test", ActiveStatus, now))
            .ToList();

        foreach (var batch in entities.Chunk(TransactionBatchSize))
        {
            var actions = batch.Select(entity => new TableTransactionAction(TableTransactionActionType.Add, entity));
            await table.SubmitTransactionAsync(actions, cancellationToken);
        }
    }

    public static async Task<TableEntity?> GetAsync(TableClient table, Guid oid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);

        var result = await table.GetEntityIfExistsAsync<TableEntity>(PartitionKey, oid.ToString("D"), cancellationToken: cancellationToken);
        return result.HasValue ? result.Value : null;
    }

    public static async Task<int> CountAsync(TableClient table, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);

        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {PartitionKey}");
        var count = 0;
        await foreach (var entity in table.QueryAsync<TableEntity>(filter, select: [nameof(TableEntity.RowKey)], cancellationToken: cancellationToken))
        {
            count++;
        }

        return count;
    }

    private static TableEntity Build(Guid oid, string displayName, string? email, string status, DateTimeOffset lastSignInAt)
    {
        var entity = new TableEntity(PartitionKey, oid.ToString("D"))
        {
            [UserColumns.DisplayName] = displayName,
            [UserColumns.Status] = status,
            [UserColumns.CreatedAt] = lastSignInAt,
            [UserColumns.LastSignInAt] = lastSignInAt,
        };
        if (email is not null)
        {
            entity[UserColumns.Email] = email;
        }

        return entity;
    }
}
