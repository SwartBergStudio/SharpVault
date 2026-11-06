// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Sharing;

public sealed partial class TableShareInboxStore(TableServiceClient tableServiceClient, ILogger<TableShareInboxStore> logger) : IShareInboxStore
{
    private const int BadRequestStatus = 400;
    private const int ConflictStatus = 409;
    private const int NotFoundStatus = 404;
    private const int PreconditionFailedStatus = 412;
    private const int RemoveAttempts = 2;

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.ShareInbox);

    public async Task AddIfAbsentAsync(ShareInboxEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            await _table.AddEntityAsync(entry.Adapt<ShareInboxTableEntity>(), cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == ConflictStatus)
        {
            LogAlreadyPresent(logger);
        }
    }

    public async Task<Versioned<ShareInboxEntry>?> GetAsync(
        Guid recipientId,
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<ShareInboxTableEntity>(
            ShareInboxKeys.PartitionKey(recipientId),
            ShareInboxKeys.RowKey(ownerId, kind, itemId),
            cancellationToken: cancellationToken);

        if (!response.HasValue || response.Value is not { } entity)
        {
            return null;
        }

        var entries = ReadEntries([entity]);
        return entries.Count == 1 ? entries[0] : null;
    }

    public async Task<StoreWriteResult> SetFiledFolderAsync(ShareInboxEntry entry, string etag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        try
        {
            await _table.UpdateEntityAsync(entry.Adapt<ShareInboxTableEntity>(), new ETag(etag), TableUpdateMode.Replace, cancellationToken);
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

    public async Task<bool> RemoveAsync(
        Guid recipientId,
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var partitionKey = ShareInboxKeys.PartitionKey(recipientId);
        var rowKey = ShareInboxKeys.RowKey(ownerId, kind, itemId);

        for (var attempt = 1; ; attempt++)
        {
            var response = await _table.GetEntityIfExistsAsync<ShareInboxTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken);
            if (!response.HasValue || response.Value is not { } entity)
            {
                return false;
            }

            try
            {
                await _table.DeleteEntityAsync(partitionKey, rowKey, entity.ETag, cancellationToken);
                return true;
            }
            catch (RequestFailedException exception) when (exception.Status == NotFoundStatus)
            {
                return false;
            }
            catch (RequestFailedException exception) when (exception.Status == PreconditionFailedStatus && attempt < RemoveAttempts)
            {
                LogRemoveRetry(logger);
            }
        }
    }

    public async Task<ShareInboxPage> ListAsync(
        Guid recipientId,
        Guid? filedFolderId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var (afterKey, storageToken) = ParseToken(continuationToken);
        var filter = BuildFilter(recipientId, filedFolderId, afterKey);

        try
        {
            return await ReadPageAsync(filter, pageSize, storageToken, cancellationToken);
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }
    }

    private async Task<ShareInboxPage> ReadPageAsync(string filter, int pageSize, string? storageToken, CancellationToken cancellationToken)
    {
        var requests = 0;
        string? pendingToken = null;

        await foreach (var page in _table
            .QueryAsync<ShareInboxTableEntity>(filter, maxPerPage: pageSize, cancellationToken: cancellationToken)
            .AsPages(storageToken)
            .WithCancellation(cancellationToken))
        {
            requests++;
            if (page.Values.Count > 0)
            {
                var next = page.ContinuationToken is null ? null : ContinuationTokens.ForInbox(page.Values[^1].RowKey);
                return new ShareInboxPage(ReadEntries(page.Values), next);
            }

            pendingToken = page.ContinuationToken;
            if (pendingToken is null || requests >= VaultStorageLimits.MaxStorageRequestsPerListCall)
            {
                break;
            }
        }

        return new ShareInboxPage([], pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
    }

    private static string BuildFilter(Guid recipientId, Guid? filedFolderId, string? afterKey)
    {
        var partitionKey = ShareInboxKeys.PartitionKey(recipientId);

        return (afterKey, filedFolderId) switch
        {
            (null, null) => TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}"),
            ({ } after, null) => TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey} and RowKey gt {after}"),
            (null, { } folderId) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partitionKey} and FiledFolderKey eq {VaultKeys.FolderKey(folderId)}"),
            ({ } after, { } folderId) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partitionKey} and RowKey gt {after} and FiledFolderKey eq {VaultKeys.FolderKey(folderId)}"),
        };
    }

    private static (string? AfterKey, string? StorageToken) ParseToken(string? token)
    {
        if (token is null)
        {
            return (null, null);
        }

        if (ContinuationTokens.TryGetInboxKey(token, out var key))
        {
            return (key, null);
        }

        if (ContinuationTokens.TryGetStorageToken(token, out var storageToken))
        {
            return (null, storageToken);
        }

        throw new ArgumentException("The continuation token is not valid.", nameof(token));
    }

    private List<Versioned<ShareInboxEntry>> ReadEntries(IEnumerable<ShareInboxTableEntity> entities)
    {
        var entries = new List<Versioned<ShareInboxEntry>>();
        var damaged = 0;

        foreach (var entity in entities)
        {
            try
            {
                entries.Add(new Versioned<ShareInboxEntry>(ShareInboxMappingRegister.ToDomain(entity), entity.ETag.ToString()));
            }
            catch (Exception exception) when (exception is InvalidDataException or FormatException)
            {
                damaged++;
            }
        }

        if (damaged > 0)
        {
            LogDamagedEntries(logger, damaged);
        }

        return entries;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {DamagedEntryCount} share inbox rows that could not be read")]
    private static partial void LogDamagedEntries(ILogger logger, int damagedEntryCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Share inbox row already present")]
    private static partial void LogAlreadyPresent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Share inbox removal retried after a concurrent change")]
    private static partial void LogRemoveRetry(ILogger logger);
}
