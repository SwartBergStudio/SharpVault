// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Runtime.ExceptionServices;
using Azure;
using Azure.Data.Tables;
using Mapster;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Storage;

namespace SharpVault.Infrastructure.Vaults;

public sealed partial class TableVaultStore(TableServiceClient tableServiceClient, ILogger<TableVaultStore> logger) : IVaultStore
{
    private const int BadRequestStatus = 400;
    private const int ConflictStatus = 409;
    private const int PreconditionFailedStatus = 412;
    private const int NotFoundStatus = 404;
    private const int BinKeyPrefixLength = 1;

    private const string RowKeyColumn = "RowKey";
    private const string CreatedAtColumn = "CreatedAt";
    private const string FolderKeyColumn = "FolderKey";
    private const string ParentKeyColumn = "ParentKey";
    private const string GuidTextFormat = "N";

    private static readonly string[] InventoryColumns = [RowKeyColumn, FolderKeyColumn, ParentKeyColumn, CreatedAtColumn];

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.Vaults);

    public async Task<Versioned<VaultKeyRing>?> GetKeyRingAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<VaultKeyRingTableEntity>(
            VaultKeys.PartitionKey(ownerId),
            VaultKeys.KeyRingRowKey,
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity
            ? new Versioned<VaultKeyRing>(entity.Adapt<VaultKeyRing>(), entity.ETag.ToString())
            : null;
    }

    public async Task<Versioned<VaultKeyRing>?> AddKeyRingAsync(VaultKeyRing keyRing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyRing);

        try
        {
            var response = await _table.AddEntityAsync(keyRing.Adapt<VaultKeyRingTableEntity>(), cancellationToken);
            var etag = response.Headers.ETag?.ToString();
            return etag is null
                ? await GetKeyRingAsync(keyRing.OwnerId, cancellationToken)
                : new Versioned<VaultKeyRing>(keyRing, etag);
        }
        catch (RequestFailedException exception) when (exception.Status == ConflictStatus)
        {
            return null;
        }
    }

    public async Task<Versioned<Secret>?> GetSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        await GetSecretRowAsync(ownerId, VaultKeys.SecretRowKey(secretId, recycled: false), cancellationToken);

    public async Task<Versioned<Secret>?> GetRecycledSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        await GetSecretRowAsync(ownerId, VaultKeys.SecretRowKey(secretId, recycled: true), cancellationToken);

    public async Task<SecretPage> ListSecretsAsync(
        Guid ownerId,
        SecretFolderFilter filter,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var (afterSecretId, storageToken) = ParseSecretToken(continuationToken);
        var query = SecretRangeFilter(ownerId, filter, afterSecretId);

        try
        {
            return await ReadSecretPageAsync(query, pageSize, storageToken, cancellationToken);
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }
    }

    private async Task<SecretPage> ReadSecretPageAsync(string query, int pageSize, string? storageToken, CancellationToken cancellationToken)
    {
        var requests = 0;
        string? pendingToken = null;

        await foreach (var page in _table
            .QueryAsync<SecretTableEntity>(query, maxPerPage: pageSize, cancellationToken: cancellationToken)
            .AsPages(storageToken)
            .WithCancellation(cancellationToken))
        {
            requests++;
            var items = page.Values.Select(ToVersionedSecret).ToList();
            if (items.Count > 0)
            {
                var next = page.ContinuationToken is null ? null : ContinuationTokens.ForSecret(items[^1].Value.Id);
                return new SecretPage(items, next);
            }

            pendingToken = page.ContinuationToken;
            if (pendingToken is null || requests >= VaultStorageLimits.MaxStorageRequestsPerListCall)
            {
                break;
            }
        }

        return new SecretPage([], pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
    }

    public async Task<IReadOnlyList<Versioned<Secret>>> ListSecretsInFolderAsync(
        Guid ownerId,
        Guid? folderId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var query = SecretRangeFilter(ownerId, folderId is { } id ? SecretFolderFilter.InFolder(id) : SecretFolderFilter.Unfiled, null);
        var items = new List<Versioned<Secret>>();

        await foreach (var page in _table
            .QueryAsync<SecretTableEntity>(query, maxPerPage: Math.Min(limit, VaultStorageLimits.FolderPageSize), cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            items.AddRange(page.Values.Select(ToVersionedSecret));
            if (items.Count >= limit)
            {
                return items.Take(limit).ToList();
            }
        }

        return items;
    }

    public async Task<Versioned<Folder>?> GetFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        await GetFolderRowAsync(ownerId, VaultKeys.FolderRowKey(folderId, recycled: false), cancellationToken);

    public async Task<Versioned<Folder>?> GetRecycledFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        await GetFolderRowAsync(ownerId, VaultKeys.FolderRowKey(folderId, recycled: true), cancellationToken);

    public async Task<IReadOnlyList<Versioned<Folder>>> ListFoldersAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {VaultKeys.FolderPrefix} and RowKey lt {VaultKeys.UpperBound(VaultKeys.FolderPrefix)}");

        var folders = new List<Versioned<Folder>>();
        await foreach (var page in _table
            .QueryAsync<FolderTableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            folders.AddRange(page.Values.Select(ToVersionedFolder));
        }

        return folders;
    }

    public async Task<Versioned<SecretVersion>?> GetVersionAsync(Guid ownerId, Guid secretId, Guid versionId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<SecretVersionTableEntity>(
            VaultKeys.PartitionKey(ownerId),
            VaultKeys.VersionRowKey(secretId, versionId),
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity
            ? new Versioned<SecretVersion>(entity.Adapt<SecretVersion>(), entity.ETag.ToString())
            : null;
    }

    public async Task<IReadOnlyList<Versioned<SecretVersion>>> ListVersionsAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken)
    {
        var start = VaultKeys.VersionRangeStart(secretId);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {start} and RowKey lt {VaultKeys.UpperBound(start)}");

        var versions = new List<Versioned<SecretVersion>>();
        await foreach (var page in _table
            .QueryAsync<SecretVersionTableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            versions.AddRange(page.Values.Select(entity => new Versioned<SecretVersion>(entity.Adapt<SecretVersion>(), entity.ETag.ToString())));
        }

        return versions;
    }

    public async Task<RecycleBinPage> ListRecycleBinAsync(Guid ownerId, int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var (afterKey, storageToken) = ParseBinToken(continuationToken);
        var partitionKey = VaultKeys.PartitionKey(ownerId);
        var upper = VaultKeys.RecycleBinUpperBound;
        var lowerAfter = VaultKeys.RecycleBinLowerBound + afterKey;
        var filter = afterKey is null
            ? TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey} and RowKey ge {VaultKeys.RecycleBinLowerBound} and RowKey lt {upper}")
            : TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey} and RowKey gt {lowerAfter} and RowKey lt {upper}");

        try
        {
            return await ReadBinPageAsync(filter, pageSize, storageToken, cancellationToken);
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }
    }

    private async Task<RecycleBinPage> ReadBinPageAsync(string filter, int pageSize, string? storageToken, CancellationToken cancellationToken)
    {
        var requests = 0;
        string? pendingToken = null;
        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: pageSize, cancellationToken: cancellationToken)
            .AsPages(storageToken)
            .WithCancellation(cancellationToken))
        {
            requests++;
            var entries = page.Values.Select(ToBinEntry).ToList();
            if (entries.Count > 0)
            {
                var next = page.ContinuationToken is null ? null : page.Values[^1].RowKey[BinKeyPrefixLength..];
                return new RecycleBinPage(entries, next);
            }

            pendingToken = page.ContinuationToken;
            if (pendingToken is null || requests >= VaultStorageLimits.MaxStorageRequestsPerListCall)
            {
                break;
            }
        }

        return new RecycleBinPage([], pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
    }

    public async Task<RecycledBatch> ListRecycledBatchAsync(Guid ownerId, Guid deletionBatchId, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {VaultKeys.RecycleBinLowerBound} and RowKey lt {VaultKeys.RecycleBinUpperBound} and DeletionBatchId eq {deletionBatchId.ToString("D", CultureInfo.InvariantCulture)}");

        return await ReadRecycledAsync(filter, cancellationToken);
    }

    public async Task<RecycledBatch> ListRecycledBeforeAsync(Guid ownerId, DateTimeOffset deletedBefore, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {VaultKeys.RecycleBinLowerBound} and RowKey lt {VaultKeys.RecycleBinUpperBound} and DeletedAt lt {deletedBefore}");

        return await ReadRecycledAsync(filter, cancellationToken);
    }

    public async Task<Versioned<ShareGrant>?> GetShareGrantAsync(
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        Guid recipientId,
        CancellationToken cancellationToken) =>
        await GetGrantRowAsync(ownerId, VaultKeys.GrantByItemRowKey(kind, itemId, recipientId), cancellationToken);

    public async Task<ShareGrantRows?> GetShareGrantRowsAsync(
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        Guid recipientId,
        CancellationToken cancellationToken)
    {
        var byItemTask = GetGrantRowAsync(ownerId, VaultKeys.GrantByItemRowKey(kind, itemId, recipientId), cancellationToken);
        var byRecipientTask = GetGrantRowAsync(ownerId, VaultKeys.GrantByRecipientRowKey(recipientId, kind, itemId), cancellationToken);
        await Task.WhenAll(byItemTask, byRecipientTask);

        var byItem = await byItemTask;
        var byRecipient = await byRecipientTask;

        return byItem is not null && byRecipient is not null
            ? new ShareGrantRows(byItem, byRecipient.ETag)
            : null;
    }

    public async Task<string?> GetShareGrantByRecipientETagAsync(
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        Guid recipientId,
        CancellationToken cancellationToken)
    {
        var row = await GetGrantRowAsync(ownerId, VaultKeys.GrantByRecipientRowKey(recipientId, kind, itemId), cancellationToken);
        return row?.ETag;
    }

    public async Task<IReadOnlyList<Versioned<ShareGrant>>> ListShareGrantsForItemAsync(
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var start = VaultKeys.GrantItemRangeStart(kind, itemId);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {start} and RowKey lt {VaultKeys.UpperBound(start)}");

        var grants = new List<Versioned<ShareGrant>>();
        await foreach (var page in _table
            .QueryAsync<ShareGrantTableEntity>(filter, maxPerPage: Math.Min(limit, VaultStorageLimits.FolderPageSize), cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            grants.AddRange(ReadGrants(page.Values));
            if (grants.Count >= limit)
            {
                return grants.Take(limit).ToList();
            }
        }

        return grants;
    }

    public async Task<IReadOnlyList<ShareGrant>> ListFolderGrantsToRecipientAsync(Guid ownerId, Guid recipientId, CancellationToken cancellationToken)
    {
        var start = VaultKeys.GrantRecipientFolderRangeStart(recipientId);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {start} and RowKey lt {VaultKeys.UpperBound(start)}");

        return await ReadGrantRangeAsync(filter, cancellationToken);
    }

    public async Task<IReadOnlyList<ShareGrant>> ListGrantsToRecipientBetweenAsync(
        Guid ownerId,
        Guid recipientId,
        ShareItemRef first,
        ShareItemRef last,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);

        var lower = VaultKeys.GrantByRecipientRowKey(recipientId, first.Kind, first.ItemId);
        var upper = VaultKeys.GrantByRecipientRowKey(recipientId, last.Kind, last.ItemId);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {lower} and RowKey le {upper}");

        return await ReadGrantRangeAsync(filter, cancellationToken);
    }

    public async Task<VaultInventoryPage> ListInventoryAsync(Guid ownerId, string? continuationToken, CancellationToken cancellationToken)
    {
        var storageToken = ParseStorageOnlyToken(continuationToken);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and ((RowKey ge {VaultKeys.InventoryLowerBound} and RowKey lt {VaultKeys.GrantRangeLowerBound}) or (RowKey ge {VaultKeys.GrantRangeUpperBound} and RowKey lt {VaultKeys.InventoryUpperBound}))");

        try
        {
            return await ReadInventoryPageAsync(filter, storageToken, cancellationToken);
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }
    }

    private async Task<VaultInventoryPage> ReadInventoryPageAsync(string filter, string? storageToken, CancellationToken cancellationToken)
    {
        var requests = 0;
        string? pendingToken = null;

        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, select: InventoryColumns, cancellationToken: cancellationToken)
            .AsPages(storageToken)
            .WithCancellation(cancellationToken))
        {
            requests++;
            pendingToken = page.ContinuationToken;
            if (page.Values.Count > 0)
            {
                return new VaultInventoryPage(
                    [.. page.Values.Select(ToInventoryRow)],
                    pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
            }

            if (pendingToken is null || requests >= VaultStorageLimits.MaxStorageRequestsPerListCall)
            {
                break;
            }
        }

        return new VaultInventoryPage([], pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
    }

    private static VaultInventoryRow ToInventoryRow(TableEntity entity)
    {
        var rowKey = entity.RowKey;
        var etag = entity.ETag.ToString();
        var createdAt = entity.GetDateTimeOffset(CreatedAtColumn) ?? default;

        if (rowKey.StartsWith(VaultKeys.VersionPrefix, StringComparison.Ordinal))
        {
            var secretText = rowKey[VaultKeys.VersionPrefix.Length..rowKey.LastIndexOf('_')];
            var versionText = rowKey[(rowKey.LastIndexOf('_') + 1)..];
            return new VaultInventoryRow(VaultItemKind.Version, ParseInventoryId(versionText), ParseInventoryId(secretText), createdAt, etag);
        }

        var (kind, prefix) = InventoryKindOf(rowKey);
        var itemId = ParseInventoryId(rowKey[prefix.Length..]);
        var containerKey = kind is VaultItemKind.ActiveSecret or VaultItemKind.RecycledSecret
            ? entity.GetString(FolderKeyColumn)
            : entity.GetString(ParentKeyColumn);

        return new VaultInventoryRow(kind, itemId, VaultKeys.ParseFolderKey(containerKey), createdAt, etag);
    }

    private static (VaultItemKind Kind, string Prefix) InventoryKindOf(string rowKey) => rowKey switch
    {
        _ when rowKey.StartsWith(VaultKeys.SecretPrefix, StringComparison.Ordinal) => (VaultItemKind.ActiveSecret, VaultKeys.SecretPrefix),
        _ when rowKey.StartsWith(VaultKeys.RecycledSecretPrefix, StringComparison.Ordinal) => (VaultItemKind.RecycledSecret, VaultKeys.RecycledSecretPrefix),
        _ when rowKey.StartsWith(VaultKeys.FolderPrefix, StringComparison.Ordinal) => (VaultItemKind.ActiveFolder, VaultKeys.FolderPrefix),
        _ when rowKey.StartsWith(VaultKeys.RecycledFolderPrefix, StringComparison.Ordinal) => (VaultItemKind.RecycledFolder, VaultKeys.RecycledFolderPrefix),
        _ => throw new InvalidDataException("A vault row has a key prefix the inventory does not know."),
    };

    private static Guid ParseInventoryId(string text) =>
        Guid.TryParseExact(text, GuidTextFormat, out var id) ? id : throw new InvalidDataException("A vault row key does not end in an item id.");

    public async Task<ShareGrantPage> ListShareGrantsByOwnerAsync(
        Guid ownerId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var storageToken = ParseStorageOnlyToken(continuationToken);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {VaultKeys.GrantByItemPrefix} and RowKey lt {VaultKeys.UpperBound(VaultKeys.GrantByItemPrefix)}");

        try
        {
            return await ReadGrantPageAsync(filter, pageSize, storageToken, cancellationToken);
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }
    }

    public async Task<StoredCount> CountShareGrantsByOwnerAsync(Guid ownerId, int maxPages, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPages, 1);

        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {VaultKeys.GrantByItemPrefix} and RowKey lt {VaultKeys.UpperBound(VaultKeys.GrantByItemPrefix)}");
        var pages = _table.QueryAsync<TableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, select: [RowKeyColumn], cancellationToken: cancellationToken)
            .AsPages();

        var count = 0;
        var readPages = 0;
        await foreach (var page in pages.WithCancellation(cancellationToken))
        {
            count += page.Values.Count;
            readPages++;
            if (readPages == maxPages)
            {
                return new StoredCount(count, page.ContinuationToken is not null);
            }
        }

        return new StoredCount(count, false);
    }

    private async Task<ShareGrantPage> ReadGrantPageAsync(string filter, int pageSize, string? storageToken, CancellationToken cancellationToken)
    {
        var requests = 0;
        string? pendingToken = null;

        await foreach (var page in _table
            .QueryAsync<ShareGrantTableEntity>(filter, maxPerPage: pageSize, cancellationToken: cancellationToken)
            .AsPages(storageToken)
            .WithCancellation(cancellationToken))
        {
            requests++;
            pendingToken = page.ContinuationToken;
            var items = ReadGrants(page.Values);
            if (items.Count > 0 || page.Values.Count > 0)
            {
                return new ShareGrantPage(items, pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
            }

            if (pendingToken is null || requests >= VaultStorageLimits.MaxStorageRequestsPerListCall)
            {
                break;
            }
        }

        return new ShareGrantPage([], pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
    }

    private async Task<IReadOnlyList<ShareGrant>> ReadGrantRangeAsync(string filter, CancellationToken cancellationToken)
    {
        var grants = new List<ShareGrant>();
        await foreach (var page in _table
            .QueryAsync<ShareGrantTableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            grants.AddRange(ReadGrants(page.Values).Select(grant => grant.Value));
        }

        return grants;
    }

    private async Task<Versioned<ShareGrant>?> GetGrantRowAsync(Guid ownerId, string rowKey, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<ShareGrantTableEntity>(
            VaultKeys.PartitionKey(ownerId),
            rowKey,
            cancellationToken: cancellationToken);

        if (!response.HasValue || response.Value is not { } entity)
        {
            return null;
        }

        var grants = ReadGrants([entity]);
        return grants.Count == 1 ? grants[0] : null;
    }

    private List<Versioned<ShareGrant>> ReadGrants(IEnumerable<ShareGrantTableEntity> entities)
    {
        var grants = new List<Versioned<ShareGrant>>();
        var damaged = 0;

        foreach (var entity in entities)
        {
            try
            {
                grants.Add(new Versioned<ShareGrant>(VaultStorageMappingRegister.ToDomain(entity), entity.ETag.ToString()));
            }
            catch (Exception exception) when (exception is InvalidDataException or FormatException)
            {
                damaged++;
            }
        }

        if (damaged > 0)
        {
            LogDamagedGrants(logger, damaged);
        }

        return grants;
    }

    private static string? ParseStorageOnlyToken(string? token)
    {
        if (token is null)
        {
            return null;
        }

        return ContinuationTokens.TryGetStorageToken(token, out var storageToken)
            ? storageToken
            : throw new InvalidContinuationTokenException();
    }

    public async Task<VaultWriteResult> SubmitAsync(Guid ownerId, IReadOnlyList<VaultChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var chunks = VaultTransactionPlanner.Plan(ownerId, changes);
        var etags = new string?[changes.Count];
        var completed = 0;
        var partitionKey = VaultKeys.PartitionKey(ownerId);
        var hasKeyRingTouch = changes.Count > 0 && changes[0].Kind == VaultChangeKind.TouchKeyRing;
        var keyRingETag = hasKeyRingTouch ? changes[0].ETag : null;

        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            var actions = WithCurrentKeyRingETag(chunk, partitionKey, keyRingETag);

            try
            {
                var response = await _table.SubmitTransactionAsync(actions, cancellationToken);
                RecordETags(chunk, response.Value, etags);
                completed += chunk.ChangeIndexes.Count;
                if (hasKeyRingTouch)
                {
                    keyRingETag = response.Value[0].Headers.ETag?.ToString() ?? keyRingETag;
                    etags[0] = keyRingETag;
                }
            }
            catch (TableTransactionFailedException exception) when (MapFailure(exception.Status) is { } failure)
            {
                if (completed > 0)
                {
                    LogPartialWrite(logger, completed, changes.Count, failure);
                }

                return new VaultWriteResult(failure, completed, etags);
            }
            catch (Exception exception) when (completed > 0)
            {
                LogInterruptedWrite(logger, completed, changes.Count, exception.GetType().Name);
                return new VaultWriteResult(StoreWriteResult.Conflict, completed, etags, ExceptionDispatchInfo.Capture(exception));
            }
        }

        return new VaultWriteResult(StoreWriteResult.Written, completed, etags);
    }

    private static IReadOnlyList<TableTransactionAction> WithCurrentKeyRingETag(PlannedTransaction chunk, string partitionKey, string? keyRingETag)
    {
        if (!chunk.LeadsWithInjectedKeyRingTouch || keyRingETag is null)
        {
            return chunk.Actions;
        }

        var actions = chunk.Actions.ToList();
        actions[0] = VaultTransactionPlanner.KeyRingTouch(partitionKey, keyRingETag);
        return actions;
    }

    private static void RecordETags(PlannedTransaction chunk, IReadOnlyList<Response> responses, string?[] etags)
    {
        for (var position = 0; position < chunk.ChangeIndexes.Count; position++)
        {
            if (chunk.PrimaryActionIndexes[position] is { } actionIndex)
            {
                etags[chunk.ChangeIndexes[position]] = responses[actionIndex].Headers.ETag?.ToString();
            }
        }
    }

    private static StoreWriteResult? MapFailure(int status) => status switch
    {
        PreconditionFailedStatus => StoreWriteResult.Conflict,
        NotFoundStatus => StoreWriteResult.NotFound,
        ConflictStatus => StoreWriteResult.AlreadyExists,
        _ => null,
    };

    private static (Guid? AfterSecretId, string? StorageToken) ParseSecretToken(string? token)
    {
        if (token is null)
        {
            return (null, null);
        }

        if (ContinuationTokens.TryGetSecretId(token, out var secretId))
        {
            return (secretId, null);
        }

        if (ContinuationTokens.TryGetStorageToken(token, out var storageToken))
        {
            return (null, storageToken);
        }

        throw new ArgumentException("The continuation token is not valid.", nameof(token));
    }

    private static (string? AfterKey, string? StorageToken) ParseBinToken(string? token)
    {
        if (token is null)
        {
            return (null, null);
        }

        if (ContinuationTokens.TryGetBinKey(token, out var key))
        {
            return (key, null);
        }

        if (ContinuationTokens.TryGetStorageToken(token, out var storageToken))
        {
            return (null, storageToken);
        }

        throw new ArgumentException("The continuation token is not valid.", nameof(token));
    }

    private static string SecretRangeFilter(Guid ownerId, SecretFolderFilter filter, Guid? afterSecretId)
    {
        var partitionKey = VaultKeys.PartitionKey(ownerId);
        var inclusive = afterSecretId is null;
        var lower = afterSecretId is { } after ? VaultKeys.SecretRowKey(after, recycled: false) : VaultKeys.SecretPrefix;
        var upper = VaultKeys.UpperBound(VaultKeys.SecretPrefix);
        var folderKey = filter.Scope switch
        {
            SecretFolderScope.All => null,
            SecretFolderScope.Unfiled => VaultKeys.RootFolderKey,
            SecretFolderScope.Folder when filter.FolderId is { } folderId => VaultKeys.FolderKey(folderId),
            _ => throw new ArgumentException("The folder filter is not valid.", nameof(filter)),
        };

        return (inclusive, folderKey) switch
        {
            (true, null) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partitionKey} and RowKey ge {lower} and RowKey lt {upper}"),
            (false, null) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partitionKey} and RowKey gt {lower} and RowKey lt {upper}"),
            (true, { } key) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partitionKey} and RowKey ge {lower} and RowKey lt {upper} and FolderKey eq {key}"),
            (false, { } key) => TableClient.CreateQueryFilter(
                $"PartitionKey eq {partitionKey} and RowKey gt {lower} and RowKey lt {upper} and FolderKey eq {key}"),
        };
    }

    private static Versioned<Secret> ToVersionedSecret(SecretTableEntity entity) =>
        new(entity.Adapt<Secret>(), entity.ETag.ToString());

    private static Versioned<Folder> ToVersionedFolder(FolderTableEntity entity) =>
        new(entity.Adapt<Folder>(), entity.ETag.ToString());

    private static RecycleBinEntry ToBinEntry(TableEntity entity) =>
        entity.RowKey.StartsWith(VaultKeys.RecycledFolderPrefix, StringComparison.Ordinal)
            ? new RecycleBinEntry(null, ToVersionedFolder(FolderTableEntity.From(entity)))
            : new RecycleBinEntry(ToVersionedSecret(SecretTableEntity.From(entity)), null);

    private async Task<Versioned<Secret>?> GetSecretRowAsync(Guid ownerId, string rowKey, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<SecretTableEntity>(
            VaultKeys.PartitionKey(ownerId),
            rowKey,
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity ? ToVersionedSecret(entity) : null;
    }

    private async Task<Versioned<Folder>?> GetFolderRowAsync(Guid ownerId, string rowKey, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<FolderTableEntity>(
            VaultKeys.PartitionKey(ownerId),
            rowKey,
            cancellationToken: cancellationToken);

        return response.HasValue && response.Value is { } entity ? ToVersionedFolder(entity) : null;
    }

    private async Task<RecycledBatch> ReadRecycledAsync(string filter, CancellationToken cancellationToken)
    {
        var secrets = new List<Versioned<Secret>>();
        var folders = new List<Versioned<Folder>>();

        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            foreach (var entry in page.Values.Select(ToBinEntry))
            {
                if (entry.Folder is not null)
                {
                    folders.Add(entry.Folder);
                }
                else if (entry.Secret is not null)
                {
                    secrets.Add(entry.Secret);
                }
            }
        }

        return new RecycledBatch(secrets, folders);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {DamagedGrantCount} share grant rows that could not be read")]
    private static partial void LogDamagedGrants(ILogger logger, int damagedGrantCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vault write stopped after {CompletedChanges} of {TotalChanges} changes with {Result}")]
    private static partial void LogPartialWrite(ILogger logger, int completedChanges, int totalChanges, StoreWriteResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vault write interrupted after {CompletedChanges} of {TotalChanges} changes by {ExceptionType}")]
    private static partial void LogInterruptedWrite(ILogger logger, int completedChanges, int totalChanges, string exceptionType);
}
