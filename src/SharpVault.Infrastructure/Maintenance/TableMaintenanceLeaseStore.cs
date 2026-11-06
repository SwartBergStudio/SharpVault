// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Maintenance;
using SharpVault.Infrastructure.Storage;

namespace SharpVault.Infrastructure.Maintenance;

public sealed class TableMaintenanceLeaseStore(TableServiceClient tableServiceClient, TimeProvider timeProvider) : IMaintenanceLeaseStore
{
    private const int ConflictStatus = 409;
    private const int PreconditionFailedStatus = 412;
    private const int NotFoundStatus = 404;
    private const string GuidFormat = "D";

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.Maintenance);

    public async Task<Versioned<MaintenanceLease>?> TryAcquireAsync(string name, string holder, TimeSpan duration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        var now = timeProvider.GetUtcNow();
        var existing = await _table.GetEntityIfExistsAsync<MaintenanceLeaseTableEntity>(
            MaintenanceKeys.PartitionKey,
            name,
            cancellationToken: cancellationToken);

        if (!existing.HasValue || existing.Value is null)
        {
            return await AddAsync(name, holder, now + duration, cancellationToken);
        }

        var current = existing.Value;
        if (current.Holder != holder && current.ExpiresAt > now)
        {
            return null;
        }

        var etag = current.ETag;
        current.Holder = holder;
        current.ExpiresAt = now + duration;
        return await ReplaceAsync(current, etag, cancellationToken);
    }

    public async Task<Versioned<MaintenanceLease>?> RenewAsync(MaintenanceLease lease, string etag, TimeSpan duration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        var entity = ToEntity(lease);
        entity.ExpiresAt = timeProvider.GetUtcNow() + duration;
        return await ReplaceAsync(entity, new ETag(etag), cancellationToken);
    }

    public async Task ReleaseAsync(MaintenanceLease lease, string etag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        var entity = ToEntity(lease);
        entity.ExpiresAt = timeProvider.GetUtcNow();
        await ReplaceAsync(entity, new ETag(etag), cancellationToken);
    }

    private static MaintenanceLeaseTableEntity ToEntity(MaintenanceLease lease) => new()
    {
        PartitionKey = MaintenanceKeys.PartitionKey,
        RowKey = lease.Name,
        Holder = lease.Holder,
        ExpiresAt = lease.ExpiresAt,
        RunId = lease.RunId?.ToString(GuidFormat),
        AfterUserId = lease.AfterUserId?.ToString(GuidFormat),
        RunStartedAt = lease.RunStartedAt,
        LastCompletedAt = lease.LastCompletedAt,
    };

    private static MaintenanceLease ToLease(MaintenanceLeaseTableEntity entity) => new()
    {
        Name = entity.RowKey,
        Holder = entity.Holder,
        ExpiresAt = entity.ExpiresAt,
        RunId = Guid.TryParseExact(entity.RunId, GuidFormat, out var runId) ? runId : null,
        AfterUserId = Guid.TryParseExact(entity.AfterUserId, GuidFormat, out var afterUserId) ? afterUserId : null,
        RunStartedAt = entity.RunStartedAt,
        LastCompletedAt = entity.LastCompletedAt,
    };

    private async Task<Versioned<MaintenanceLease>?> AddAsync(string name, string holder, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var entity = new MaintenanceLeaseTableEntity
        {
            PartitionKey = MaintenanceKeys.PartitionKey,
            RowKey = name,
            Holder = holder,
            ExpiresAt = expiresAt,
        };

        try
        {
            var response = await _table.AddEntityAsync(entity, cancellationToken);
            var etag = response.Headers.ETag?.ToString();
            return etag is null ? await ReadAsync(name, cancellationToken) : new Versioned<MaintenanceLease>(ToLease(entity), etag);
        }
        catch (RequestFailedException exception) when (exception.Status == ConflictStatus)
        {
            return null;
        }
    }

    private async Task<Versioned<MaintenanceLease>?> ReplaceAsync(MaintenanceLeaseTableEntity entity, ETag etag, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.UpdateEntityAsync(entity, etag, TableUpdateMode.Replace, cancellationToken);
            var newETag = response.Headers.ETag?.ToString();
            return newETag is null ? await ReadAsync(entity.RowKey, cancellationToken) : new Versioned<MaintenanceLease>(ToLease(entity), newETag);
        }
        catch (RequestFailedException exception) when (exception.Status is PreconditionFailedStatus or NotFoundStatus)
        {
            return null;
        }
    }

    private async Task<Versioned<MaintenanceLease>?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<MaintenanceLeaseTableEntity>(
            MaintenanceKeys.PartitionKey,
            name,
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity
            ? new Versioned<MaintenanceLease>(ToLease(entity), entity.ETag.ToString())
            : null;
    }
}
