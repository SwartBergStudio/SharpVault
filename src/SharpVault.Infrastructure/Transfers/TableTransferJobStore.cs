// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Transfers;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Transfers;

public sealed class TableTransferJobStore(TableServiceClient tableServiceClient) : ITransferJobStore
{
    private const int BadRequestStatus = 400;
    private const int ConflictStatus = 409;
    private const int PreconditionFailedStatus = 412;
    private const int NotFoundStatus = 404;
    private const int PageSizeCeiling = 1000;

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.TransferJobs);

    public async Task<Versioned<TransferJob>?> GetAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<TransferJobTableEntity>(
            TransferKeys.TransferPartition,
            TransferKeys.JobRowKey(transferId),
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity
            ? new Versioned<TransferJob>(entity.Adapt<TransferJob>(), entity.ETag.ToString())
            : null;
    }

    public async Task<TransferJobPage> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var storageToken = ParseStorageToken(continuationToken);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {TransferKeys.TransferPartition} and RowKey ge {TransferKeys.JobPrefix} and RowKey lt {TransferKeys.JobRangeUpperBound}");

        try
        {
            return await ReadJobPageAsync(filter, pageSize, storageToken, cancellationToken);
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }
    }

    public async Task<StoreWriteResult> AddWithMarkerAsync(TransferJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var marker = new TransferMarker { SourceUserId = job.SourceUserId, TransferId = job.Id, TargetUserId = job.TargetUserId };
        TableTransactionAction[] actions =
        [
            new(TableTransactionActionType.Add, job.Adapt<TransferJobTableEntity>()),
            new(TableTransactionActionType.Add, marker.Adapt<TransferMarkerTableEntity>()),
        ];

        try
        {
            await _table.SubmitTransactionAsync(actions, cancellationToken);
            return StoreWriteResult.Written;
        }
        catch (TableTransactionFailedException exception) when (exception.Status == ConflictStatus)
        {
            return StoreWriteResult.AlreadyExists;
        }
    }

    public async Task<Versioned<TransferJob>?> ReplaceAsync(TransferJob job, string etag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        try
        {
            var response = await _table.UpdateEntityAsync(
                job.Adapt<TransferJobTableEntity>(),
                new ETag(etag),
                TableUpdateMode.Replace,
                cancellationToken);
            var newETag = response.Headers.ETag?.ToString();
            return newETag is null ? await GetAsync(job.Id, cancellationToken) : new Versioned<TransferJob>(job, newETag);
        }
        catch (RequestFailedException exception) when (exception.Status is PreconditionFailedStatus or NotFoundStatus)
        {
            return null;
        }
    }

    public async Task<StoreWriteResult> CloseAsync(TransferJob job, string etag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        var marker = await _table.GetEntityIfExistsAsync<TransferMarkerTableEntity>(
            TransferKeys.TransferPartition,
            TransferKeys.MarkerRowKey(job.SourceUserId),
            cancellationToken: cancellationToken);

        var actions = new List<TableTransactionAction>
        {
            new(TableTransactionActionType.UpdateReplace, job.Adapt<TransferJobTableEntity>(), new ETag(etag)),
        };

        if (marker.HasValue && marker.Value is { } entity && IsMarkerOf(entity, job.Id))
        {
            actions.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity, entity.ETag));
        }

        try
        {
            await _table.SubmitTransactionAsync(actions, cancellationToken);
            return StoreWriteResult.Written;
        }
        catch (TableTransactionFailedException exception) when (exception.Status == PreconditionFailedStatus)
        {
            return StoreWriteResult.Conflict;
        }
        catch (TableTransactionFailedException exception) when (exception.Status == NotFoundStatus)
        {
            return StoreWriteResult.NotFound;
        }
    }

    public async Task<TransferMarker?> GetMarkerAsync(Guid sourceUserId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<TransferMarkerTableEntity>(
            TransferKeys.TransferPartition,
            TransferKeys.MarkerRowKey(sourceUserId),
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity ? entity.Adapt<TransferMarker>() : null;
    }

    public async Task<IReadOnlyList<TransferMarker>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {TransferKeys.TransferPartition} and RowKey ge {TransferKeys.MarkerPrefix} and RowKey lt {TransferKeys.MarkerRangeUpperBound}");

        var markers = new List<TransferMarker>();
        await foreach (var page in _table
            .QueryAsync<TransferMarkerTableEntity>(filter, maxPerPage: PageSizeCeiling, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            markers.AddRange(page.Values.Select(entity => entity.Adapt<TransferMarker>()));
        }

        return markers;
    }

    public async Task UpsertNoticeAsync(TransferNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        await _table.UpsertEntityAsync(notice.Adapt<TransferNoticeTableEntity>(), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<IReadOnlyList<TransferNotice>> ListNoticesAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {TransferKeys.NoticePartition(userId)}");
        var notices = new List<TransferNotice>();

        await foreach (var page in _table
            .QueryAsync<TransferNoticeTableEntity>(filter, maxPerPage: Math.Min(limit, PageSizeCeiling), cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            notices.AddRange(page.Values.Select(entity => entity.Adapt<TransferNotice>()));
            if (notices.Count >= limit)
            {
                return notices.Take(limit).ToList();
            }
        }

        return notices;
    }

    public async Task<StoreWriteResult> DismissNoticeAsync(Guid userId, Guid transferId, CancellationToken cancellationToken)
    {
        var partition = TransferKeys.NoticePartition(userId);
        var rowKey = TransferKeys.NoticeRowKey(transferId);
        var existing = await _table.GetEntityIfExistsAsync<TransferNoticeTableEntity>(partition, rowKey, cancellationToken: cancellationToken);
        if (!existing.HasValue || existing.Value is not { } entity)
        {
            return StoreWriteResult.NotFound;
        }

        try
        {
            using var response = await _table.DeleteEntityAsync(partition, rowKey, entity.ETag, cancellationToken);
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

    private async Task<TransferJobPage> ReadJobPageAsync(string filter, int pageSize, string? storageToken, CancellationToken cancellationToken)
    {
        var requests = 0;
        string? pendingToken = null;

        await foreach (var page in _table
            .QueryAsync<TransferJobTableEntity>(filter, maxPerPage: pageSize, cancellationToken: cancellationToken)
            .AsPages(storageToken)
            .WithCancellation(cancellationToken))
        {
            requests++;
            pendingToken = page.ContinuationToken;
            if (page.Values.Count > 0)
            {
                return new TransferJobPage(
                    [.. page.Values.Select(entity => new Versioned<TransferJob>(entity.Adapt<TransferJob>(), entity.ETag.ToString()))],
                    pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
            }

            if (pendingToken is null || requests >= VaultStorageLimits.MaxStorageRequestsPerListCall)
            {
                break;
            }
        }

        return new TransferJobPage([], pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
    }

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

    private static bool IsMarkerOf(TransferMarkerTableEntity marker, Guid transferId) =>
        Guid.TryParseExact(marker.TransferId, "D", out var markerTransferId) && markerTransferId == transferId;
}
