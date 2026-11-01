// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Storage;

namespace SharpVault.Infrastructure.Users;

public sealed class TableUserStore(TableServiceClient tableServiceClient) : IUserStore
{
    private const int ConflictStatus = 409;
    private const int PreconditionFailedStatus = 412;
    private const int NotFoundStatus = 404;

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.Users);

    public async Task<Versioned<User>?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<UserTableEntity>(
            UserKeys.PartitionKey,
            UserKeys.RowKey(userId),
            cancellationToken: cancellationToken);

        if (!response.HasValue || response.Value is null)
        {
            return null;
        }

        return new Versioned<User>(response.Value.Adapt<User>(), response.Value.ETag.ToString());
    }

    public async Task<StoreWriteResult> AddAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            await _table.AddEntityAsync(user.Adapt<UserTableEntity>(), cancellationToken);
            return StoreWriteResult.Written;
        }
        catch (RequestFailedException exception) when (exception.Status == ConflictStatus)
        {
            return StoreWriteResult.AlreadyExists;
        }
    }

    public async Task<StoreWriteResult> ReplaceAsync(User user, string etag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        try
        {
            await _table.UpdateEntityAsync(
                user.Adapt<UserTableEntity>(),
                new ETag(etag),
                TableUpdateMode.Replace,
                cancellationToken);
            return StoreWriteResult.Written;
        }
        catch (RequestFailedException exception) when (exception.Status == PreconditionFailedStatus)
        {
            return StoreWriteResult.Conflict;
        }
        catch (RequestFailedException exception) when (exception.Status == NotFoundStatus)
        {
            return StoreWriteResult.NotFound;
        }
    }

    public async Task<UserListPage> ListAsync(int pageSize, Guid? afterUserId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var filter = afterUserId is { } after
            ? TableClient.CreateQueryFilter($"PartitionKey eq {UserKeys.PartitionKey} and RowKey gt {UserKeys.RowKey(after)}")
            : TableClient.CreateQueryFilter($"PartitionKey eq {UserKeys.PartitionKey}");

        await foreach (var page in _table
            .QueryAsync<UserTableEntity>(filter, maxPerPage: pageSize, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            var users = page.Values.Select(entity => entity.Adapt<User>()).ToList();
            return new UserListPage(users, NextAfter(page.ContinuationToken, users, afterUserId));
        }

        return new UserListPage([], null);
    }

    private static Guid? NextAfter(string? continuationToken, List<User> users, Guid? afterUserId)
    {
        if (continuationToken is null)
        {
            return null;
        }

        return users.Count > 0 ? users[^1].Id : afterUserId;
    }
}
