// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Infrastructure.Storage;

namespace SharpVault.Infrastructure.OneTimeLinks;

public sealed partial class TableOneTimeLinkStore(TableServiceClient tableServiceClient, ILogger<TableOneTimeLinkStore> logger) : IOneTimeLinkStore
{
    private const int BadRequestStatus = 400;
    private const int ConflictStatus = 409;
    private const int PreconditionFailedStatus = 412;
    private const int NotFoundStatus = 404;
    private const int PageSizeCeiling = 1000;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.OneTimeLinks);

    public async Task<StoreWriteResult> CreateAsync(OneTimeLink link, OneTimeLinkIndexEntry index, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(index);

        var expiryEntity = new OneTimeLinkExpiryEntry { LinkId = link.Id, CreatorId = link.CreatorId, ExpiresAt = link.ExpiresAt }
            .Adapt<OneTimeLinkExpiryTableEntity>();
        var indexEntity = index.Adapt<OneTimeLinkIndexTableEntity>();
        var linkEntity = link.Adapt<OneTimeLinkTableEntity>();

        await _table.AddEntityAsync(expiryEntity, cancellationToken);

        try
        {
            await _table.AddEntityAsync(indexEntity, cancellationToken);
        }
        catch (Exception)
        {
            await DeleteInsertedAsync([indexEntity]);
            throw;
        }

        try
        {
            await _table.AddEntityAsync(linkEntity, cancellationToken);
            return StoreWriteResult.Written;
        }
        catch (RequestFailedException exception) when (exception.Status == ConflictStatus)
        {
            await DeleteInsertedAsync([indexEntity]);
            return StoreWriteResult.AlreadyExists;
        }
        catch (Exception)
        {
            await DeleteInsertedAsync([indexEntity]);
            throw;
        }
    }

    public async Task<Versioned<OneTimeLink>?> GetAsync(Guid linkId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<OneTimeLinkTableEntity>(
            OneTimeLinkKeys.LinkPartition(linkId),
            OneTimeLinkKeys.LinkRowKey,
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity
            ? new Versioned<OneTimeLink>(entity.Adapt<OneTimeLink>(), entity.ETag.ToString())
            : null;
    }

    public async Task<bool> TryConsumeAsync(Guid linkId, string etag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        try
        {
            using var response = await _table.DeleteEntityAsync(
                OneTimeLinkKeys.LinkPartition(linkId),
                OneTimeLinkKeys.LinkRowKey,
                new ETag(etag),
                cancellationToken);
            return response.Status != NotFoundStatus;
        }
        catch (RequestFailedException exception) when (exception.Status is NotFoundStatus or PreconditionFailedStatus)
        {
            return false;
        }
    }

    public async Task<OneTimeLinkIndexEntry?> GetIndexEntryAsync(Guid creatorId, Guid linkId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<OneTimeLinkIndexTableEntity>(
            OneTimeLinkKeys.CreatorPartition(creatorId),
            OneTimeLinkKeys.IndexRowKey(linkId),
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity ? entity.Adapt<OneTimeLinkIndexEntry>() : null;
    }

    public async Task RemoveBookkeepingAsync(Guid creatorId, Guid linkId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await DeleteIfPresentAsync(OneTimeLinkKeys.ExpiryPartition(expiresAt), OneTimeLinkKeys.ExpiryRowKey(linkId), cancellationToken);
        await DeleteIfPresentAsync(OneTimeLinkKeys.CreatorPartition(creatorId), OneTimeLinkKeys.IndexRowKey(linkId), cancellationToken);
    }

    public async Task<OneTimeLinkIndexPage> ListIndexAsync(
        Guid creatorId,
        DateTimeOffset? activeAt,
        int pageSize,
        Guid? afterLinkId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var filter = BuildIndexFilter(OneTimeLinkKeys.CreatorPartition(creatorId), afterLinkId, activeAt);
        var wanted = pageSize + 1;
        var rows = new List<OneTimeLinkIndexEntry>();

        await foreach (var page in _table
            .QueryAsync<OneTimeLinkIndexTableEntity>(filter, maxPerPage: Math.Min(wanted, PageSizeCeiling), cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            rows.AddRange(page.Values.Select(entity => entity.Adapt<OneTimeLinkIndexEntry>()));
            if (rows.Count >= wanted)
            {
                break;
            }
        }

        var items = rows.Take(pageSize).ToList();
        var hasMore = rows.Count > pageSize;
        return new OneTimeLinkIndexPage(items, hasMore ? items[^1].LinkId : null);
    }

    public async Task AddNoticeAsync(OneTimeLinkNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        await _table.UpsertEntityAsync(notice.Adapt<OneTimeLinkNoticeTableEntity>(), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<IReadOnlyList<OneTimeLinkNotice>> ListNoticesAsync(Guid creatorId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {OneTimeLinkKeys.CreatorPartition(creatorId)} and RowKey ge {OneTimeLinkKeys.NoticePrefix} and RowKey lt {OneTimeLinkKeys.NoticeUpperBound}");
        var notices = new List<OneTimeLinkNotice>();

        await foreach (var page in _table
            .QueryAsync<OneTimeLinkNoticeTableEntity>(filter, maxPerPage: Math.Min(limit, PageSizeCeiling), cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            notices.AddRange(page.Values.Select(entity => entity.Adapt<OneTimeLinkNotice>()));
            if (notices.Count >= limit)
            {
                return notices.Take(limit).ToList();
            }
        }

        return notices;
    }

    public async Task<StoreWriteResult> DismissNoticeAsync(Guid creatorId, string noticeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(noticeId);

        if (!OneTimeLinkKeys.IsNoticeRowKey(noticeId))
        {
            return StoreWriteResult.NotFound;
        }

        var partition = OneTimeLinkKeys.CreatorPartition(creatorId);
        var existing = await _table.GetEntityIfExistsAsync<OneTimeLinkNoticeTableEntity>(partition, noticeId, cancellationToken: cancellationToken);
        if (!existing.HasValue || existing.Value is not { } entity)
        {
            return StoreWriteResult.NotFound;
        }

        try
        {
            using var response = await _table.DeleteEntityAsync(partition, noticeId, entity.ETag, cancellationToken);
            return response.Status == NotFoundStatus ? StoreWriteResult.NotFound : StoreWriteResult.Written;
        }
        catch (RequestFailedException exception) when (exception.Status == NotFoundStatus)
        {
            return StoreWriteResult.NotFound;
        }
        catch (RequestFailedException exception) when (exception.Status == PreconditionFailedStatus)
        {
            return StoreWriteResult.Conflict;
        }
    }

    public async Task<OneTimeLinkExpiryPage> ListExpiryBucketAsync(
        DateTimeOffset hour,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var storageToken = ParseStorageToken(continuationToken);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {OneTimeLinkKeys.ExpiryPartition(hour)}");

        try
        {
            await foreach (var page in _table
                .QueryAsync<OneTimeLinkExpiryTableEntity>(filter, maxPerPage: Math.Min(pageSize, PageSizeCeiling), cancellationToken: cancellationToken)
                .AsPages(storageToken)
                .WithCancellation(cancellationToken))
            {
                return new OneTimeLinkExpiryPage(
                    [.. page.Values.Select(entity => entity.Adapt<OneTimeLinkExpiryEntry>())],
                    page.ContinuationToken is null ? null : ContinuationTokens.ForStorage(page.ContinuationToken));
            }
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }

        return new OneTimeLinkExpiryPage([], null);
    }

    public async Task DeleteExpiredAsync(OneTimeLinkExpiryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await DeleteIfPresentAsync(OneTimeLinkKeys.LinkPartition(entry.LinkId), OneTimeLinkKeys.LinkRowKey, cancellationToken);
        await DeleteIfPresentAsync(OneTimeLinkKeys.CreatorPartition(entry.CreatorId), OneTimeLinkKeys.IndexRowKey(entry.LinkId), cancellationToken);
        await DeleteIfPresentAsync(OneTimeLinkKeys.ExpiryPartition(entry.ExpiresAt), OneTimeLinkKeys.ExpiryRowKey(entry.LinkId), cancellationToken);
    }

    private async Task DeleteInsertedAsync(IEnumerable<ITableEntity> entities)
    {
        using var timeout = new CancellationTokenSource(CleanupTimeout);
        foreach (var entity in entities)
        {
            try
            {
                await DeleteIfPresentAsync(entity.PartitionKey, entity.RowKey, timeout.Token);
            }
            catch (Exception exception)
            {
                LogCleanupFailed(logger, exception.GetType().Name);
            }
        }
    }

    private async Task DeleteIfPresentAsync(string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        try
        {
            await _table.DeleteEntityAsync(partitionKey, rowKey, ETag.All, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == NotFoundStatus)
        {
            return;
        }
    }

    private static string BuildIndexFilter(string partition, Guid? afterLinkId, DateTimeOffset? activeAt) =>
        (afterLinkId, activeAt) switch
        {
            (null, null) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partition} and RowKey ge {OneTimeLinkKeys.IndexPrefix} and RowKey lt {OneTimeLinkKeys.IndexUpperBound}"),
            (null, { } now) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partition} and RowKey ge {OneTimeLinkKeys.IndexPrefix} and RowKey lt {OneTimeLinkKeys.IndexUpperBound} and ExpiresAt gt {now}"),
            ({ } after, null) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partition} and RowKey gt {OneTimeLinkKeys.IndexRowKey(after)} and RowKey lt {OneTimeLinkKeys.IndexUpperBound}"),
            ({ } after, { } now) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partition} and RowKey gt {OneTimeLinkKeys.IndexRowKey(after)} and RowKey lt {OneTimeLinkKeys.IndexUpperBound} and ExpiresAt gt {now}"),
        };

    private static string? ParseStorageToken(string? token)
    {
        if (token is null)
        {
            return null;
        }

        return ContinuationTokens.TryGetStorageToken(token, out var storageToken)
            ? storageToken
            : throw new InvalidContinuationTokenException();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Cleanup after a failed one-time link write did not finish ({ExceptionType})")]
    private static partial void LogCleanupFailed(ILogger logger, string exceptionType);
}
