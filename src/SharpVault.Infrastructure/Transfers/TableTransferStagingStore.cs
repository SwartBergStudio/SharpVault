// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Transfers;

public sealed class TableTransferStagingStore(TableServiceClient tableServiceClient) : ITransferStagingStore
{
    private const int BadRequestStatus = 400;
    private const int ConflictStatus = 409;
    private const int PreconditionFailedStatus = 412;
    private const int NotFoundStatus = 404;
    private const string OwnerMismatchMessage = "A staged item belongs to a different owner than the write.";
    private const string PublishGroupShapeMessage = "A publish group needs a secret or a folder, not both.";
    private const string FenceMissingETagMessage = "The staging fence write returned no ETag.";
    private const int FenceActions = 1;

    private static readonly string[] IndexColumns =
    [
        "RowKey",
        StagingRows.StagedKindColumn,
        StagingRows.StagedSourceETagColumn,
        StagingRows.StagedVersionCountColumn,
    ];

    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.Vaults);

    public async Task<StageFence> TakeFenceAsync(Guid targetOwnerId, Guid transferId, CancellationToken cancellationToken)
    {
        var row = new TableEntity(VaultKeys.PartitionKey(targetOwnerId), VaultKeys.StagingFenceRowKey(transferId));
        var response = await _table.UpsertEntityAsync(row, TableUpdateMode.Replace, cancellationToken);
        var eTag = response.Headers.ETag?.ToString() ?? throw new InvalidOperationException(FenceMissingETagMessage);
        return new StageFence(targetOwnerId, eTag);
    }

    public async Task ReleaseFenceAsync(Guid targetOwnerId, Guid transferId, StageFence fence, CancellationToken cancellationToken)
    {
        try
        {
            await _table.DeleteEntityAsync(VaultKeys.PartitionKey(targetOwnerId), VaultKeys.StagingFenceRowKey(transferId), new ETag(fence.ETag), cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is NotFoundStatus or PreconditionFailedStatus)
        {
        }
    }

    public async Task<IReadOnlyList<StagedItemRef>> ListIndexAsync(Guid targetOwnerId, Guid transferId, CancellationToken cancellationToken)
    {
        var filter = RangeFilter(targetOwnerId, VaultKeys.StagingRangeStart(transferId));
        var items = new List<StagedItemRef>();

        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, select: IndexColumns, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            items.AddRange(page.Values.Select(row => StagingRows.ToItemRef(transferId, row)));
        }

        return items;
    }

    public async Task<IReadOnlyList<Guid>> StageSecretsAsync(Guid transferId, IReadOnlyList<StagedSecret> items, StageFence fence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var oversized = new List<Guid>();
        foreach (var owned in items.GroupBy(item => item.Secret.OwnerId))
        {
            var partitionKey = VaultKeys.PartitionKey(owned.Key);
            var groups = new List<ActionGroup>();
            foreach (var item in owned)
            {
                var group = ToStageGroup(partitionKey, transferId, item);
                if (ActionGroupPacker.FitsOneTransaction(group.Actions.Count + FenceActions, group.Bytes + VaultStorageLimits.ActionOverheadBytes) && FitsPublish(item))
                {
                    groups.Add(group);
                }
                else
                {
                    oversized.Add(item.Secret.Id);
                }
            }

            await SubmitFencedPacksAsync(transferId, fence, groups, cancellationToken);
        }

        return oversized;
    }

    public async Task StageFoldersAsync(Guid transferId, IReadOnlyList<StagedFolder> items, StageFence fence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (var owned in items.GroupBy(item => item.Folder.OwnerId))
        {
            var groups = owned
                .Select(item => new ActionGroup(
                    item.Folder.Id,
                    [new TableTransactionAction(TableTransactionActionType.UpsertReplace, StagingRows.ToFolderRow(transferId, item))],
                    ActionBytes(VaultStorageMappingRegister.ToEntity(item.Folder))))
                .ToList();
            await SubmitFencedPacksAsync(transferId, fence, groups, cancellationToken);
        }
    }

    public async Task StageSkippedAsync(Guid targetOwnerId, Guid transferId, Guid secretId, string sourceETag, StageFence fence, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceETag);

        var row = StagingRows.ToSkippedRow(VaultKeys.PartitionKey(targetOwnerId), transferId, secretId, sourceETag);
        await SubmitFencedAsync(transferId, fence, [new TableTransactionAction(TableTransactionActionType.UpsertReplace, row)], cancellationToken);
    }

    public async Task RemoveAsync(Guid targetOwnerId, Guid transferId, IReadOnlyList<StagedItemRef> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var partitionKey = VaultKeys.PartitionKey(targetOwnerId);
        foreach (var chunk in items.Chunk(VaultStorageLimits.MaxActionsPerTransaction))
        {
            var actions = chunk.Select(item => DeleteAction(partitionKey, transferId, item)).ToList();
            try
            {
                await _table.SubmitTransactionAsync(actions, cancellationToken);
            }
            catch (TableTransactionFailedException exception) when (exception.Status is NotFoundStatus or PreconditionFailedStatus)
            {
                await RemoveSinglyAsync(actions, cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<StagedFolder>> ReadFoldersAsync(Guid targetOwnerId, Guid transferId, CancellationToken cancellationToken)
    {
        var filter = RangeFilter(targetOwnerId, VaultKeys.StagingRangeStart(transferId) + VaultKeys.StagedFolderCode);
        var folders = new List<StagedFolder>();

        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            foreach (var row in page.Values)
            {
                var sourceETag = StagingRows.SourceETagOf(row);
                folders.Add(new StagedFolder(StagingRows.ToFolder(row), sourceETag));
            }
        }

        return folders;
    }

    public async Task<StagedSecretPage> ReadSecretsAsync(
        Guid targetOwnerId,
        Guid transferId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var storageToken = ParseStorageToken(continuationToken);
        var filter = RangeFilter(targetOwnerId, VaultKeys.StagingRangeStart(transferId) + VaultKeys.StagedSecretCode);

        try
        {
            return await ReadSecretPageAsync(targetOwnerId, transferId, filter, pageSize, storageToken, cancellationToken);
        }
        catch (RequestFailedException exception) when (storageToken is not null && exception.Status == BadRequestStatus)
        {
            throw new InvalidContinuationTokenException(exception);
        }
    }

    public async Task<IReadOnlyList<PublishFailure>> PublishAsync(
        Guid targetOwnerId,
        Guid transferId,
        IReadOnlyList<PublishGroup> groups,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var partitionKey = VaultKeys.PartitionKey(targetOwnerId);
        var planned = groups.Select(group => ToPublishGroup(targetOwnerId, partitionKey, transferId, group)).ToList();
        var failures = new List<PublishFailure>();

        foreach (var pack in ActionGroupPacker.Pack(planned))
        {
            try
            {
                await _table.SubmitTransactionAsync(pack.SelectMany(group => group.Actions), cancellationToken);
            }
            catch (TableTransactionFailedException exception) when (MapFailure(exception.Status) is { } failure)
            {
                if (pack.Count == 1)
                {
                    failures.Add(new PublishFailure(pack[0].ItemId, failure));
                    continue;
                }

                failures.AddRange(await PublishSinglyAsync(pack, cancellationToken));
            }
        }

        return failures;
    }

    private async Task<StagedSecretPage> ReadSecretPageAsync(
        Guid targetOwnerId,
        Guid transferId,
        string filter,
        int pageSize,
        string? storageToken,
        CancellationToken cancellationToken)
    {
        var requests = 0;
        string? pendingToken = null;

        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: pageSize, cancellationToken: cancellationToken)
            .AsPages(storageToken)
            .WithCancellation(cancellationToken))
        {
            requests++;
            pendingToken = page.ContinuationToken;
            if (page.Values.Count > 0)
            {
                var items = new List<StagedSecret>(page.Values.Count);
                foreach (var row in page.Values)
                {
                    items.Add(await ToStagedSecretAsync(targetOwnerId, transferId, row, cancellationToken));
                }

                return new StagedSecretPage(items, pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
            }

            if (pendingToken is null || requests >= VaultStorageLimits.MaxStorageRequestsPerListCall)
            {
                break;
            }
        }

        return new StagedSecretPage([], pendingToken is null ? null : ContinuationTokens.ForStorage(pendingToken));
    }

    private async Task<StagedSecret> ToStagedSecretAsync(Guid targetOwnerId, Guid transferId, TableEntity row, CancellationToken cancellationToken)
    {
        var sourceETag = StagingRows.SourceETagOf(row);
        var versionCount = StagingRows.VersionCountOf(row);
        var secret = StagingRows.ToSecret(row);
        var versions = versionCount > 0
            ? await ReadVersionsAsync(targetOwnerId, transferId, secret.Id, cancellationToken)
            : [];

        return new StagedSecret(secret, versions, [], sourceETag);
    }

    private async Task<IReadOnlyList<SecretVersion>> ReadVersionsAsync(
        Guid targetOwnerId,
        Guid transferId,
        Guid secretId,
        CancellationToken cancellationToken)
    {
        var filter = RangeFilter(targetOwnerId, VaultKeys.StagedVersionRangeStart(transferId, secretId));
        var versions = new List<SecretVersion>();

        await foreach (var page in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: VaultStorageLimits.FolderPageSize, cancellationToken: cancellationToken)
            .AsPages()
            .WithCancellation(cancellationToken))
        {
            versions.AddRange(page.Values.Select(StagingRows.ToVersion));
        }

        return versions;
    }

    private async Task RemoveSinglyAsync(IReadOnlyList<TableTransactionAction> actions, CancellationToken cancellationToken)
    {
        foreach (var action in actions)
        {
            try
            {
                await _table.SubmitTransactionAsync([action], cancellationToken);
            }
            catch (TableTransactionFailedException exception) when (exception.Status is NotFoundStatus or PreconditionFailedStatus)
            {
            }
        }
    }

    private async Task<List<PublishFailure>> PublishSinglyAsync(IReadOnlyList<ActionGroup> pack, CancellationToken cancellationToken)
    {
        var failures = new List<PublishFailure>();
        foreach (var group in pack)
        {
            try
            {
                await _table.SubmitTransactionAsync(group.Actions, cancellationToken);
            }
            catch (TableTransactionFailedException exception) when (MapFailure(exception.Status) is { } failure)
            {
                failures.Add(new PublishFailure(group.ItemId, failure));
            }
        }

        return failures;
    }

    private async Task SubmitFencedPacksAsync(Guid transferId, StageFence fence, IReadOnlyList<ActionGroup> groups, CancellationToken cancellationToken)
    {
        foreach (var pack in ActionGroupPacker.Pack(groups, FenceActions))
        {
            await SubmitFencedAsync(transferId, fence, pack.SelectMany(group => group.Actions), cancellationToken);
        }
    }

    private async Task SubmitFencedAsync(
        Guid transferId,
        StageFence fence,
        IEnumerable<TableTransactionAction> actions,
        CancellationToken cancellationToken)
    {
        var fenceRow = new TableEntity(VaultKeys.PartitionKey(fence.TargetOwnerId), VaultKeys.StagingFenceRowKey(transferId));
        var batch = new List<TableTransactionAction>
        {
            new(TableTransactionActionType.UpdateMerge, fenceRow, new ETag(fence.ETag)),
        };
        batch.AddRange(actions);

        try
        {
            var response = await _table.SubmitTransactionAsync(batch, cancellationToken);
            fence.ETag = response.Value[0].Headers.ETag?.ToString() ?? fence.ETag;
        }
        catch (TableTransactionFailedException exception) when (IsFenceLost(exception))
        {
            throw new TransferJobChangedException();
        }
    }

    private static bool IsFenceLost(TableTransactionFailedException exception) =>
        exception.Status is PreconditionFailedStatus or NotFoundStatus
        && exception.FailedTransactionActionIndex is null or 0;

    private static ActionGroup ToStageGroup(string partitionKey, Guid transferId, StagedSecret item)
    {
        var secretEntity = VaultStorageMappingRegister.ToEntity(item.Secret);
        RequireOwner(partitionKey, secretEntity.PartitionKey);

        var actions = new List<TableTransactionAction>
        {
            new(TableTransactionActionType.UpsertReplace, StagingRows.ToSecretRow(transferId, item)),
        };
        var bytes = ActionBytes(secretEntity);

        foreach (var version in item.Versions)
        {
            var versionEntity = VaultStorageMappingRegister.ToEntity(version);
            RequireOwner(partitionKey, versionEntity.PartitionKey);
            actions.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, StagingRows.ToVersionRow(transferId, version, item.SourceETag)));
            bytes += ActionBytes(versionEntity);
        }

        foreach (var stale in item.StaleVersions)
        {
            actions.Add(DeleteAction(partitionKey, transferId, stale));
            bytes += VaultStorageLimits.ActionOverheadBytes;
        }

        return new ActionGroup(item.Secret.Id, actions, bytes);
    }

    private static bool FitsPublish(StagedSecret item)
    {
        var rows = 1 + item.Versions.Count;
        var bytes = rows * VaultStorageLimits.ActionOverheadBytes
            + ActionBytes(VaultStorageMappingRegister.ToEntity(item.Secret))
            + item.Versions.Sum(version => ActionBytes(VaultStorageMappingRegister.ToEntity(version)));

        return ActionGroupPacker.FitsOneTransaction(2 * rows, bytes);
    }

    private static ActionGroup ToPublishGroup(Guid targetOwnerId, string partitionKey, Guid transferId, PublishGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (!group.MergeOnly && (group.Secret is null) == (group.Folder is null))
        {
            throw new ArgumentException(PublishGroupShapeMessage, nameof(group));
        }

        var actions = new List<TableTransactionAction>();
        var bytes = 0;
        foreach (var staged in group.StagedRows)
        {
            actions.Add(DeleteAction(partitionKey, transferId, staged));
            bytes += VaultStorageLimits.ActionOverheadBytes;
        }

        if (group.MergeOnly)
        {
            return new ActionGroup(group.ItemId, actions, bytes);
        }

        if (group.Secret is { } secret)
        {
            AddRealSecret(targetOwnerId, secret, group.Versions, actions, ref bytes);
        }
        else
        {
            var folderEntity = VaultStorageMappingRegister.ToEntity(group.Folder!);
            RequireOwner(partitionKey, folderEntity.PartitionKey);
            if (group.RecycledFolderETag is { } recycledETag)
            {
                actions.Add(new TableTransactionAction(
                    TableTransactionActionType.Delete,
                    new TableEntity(partitionKey, VaultKeys.FolderRowKey(group.ItemId, recycled: true)),
                    new ETag(recycledETag)));
                bytes += VaultStorageLimits.ActionOverheadBytes;
            }

            actions.Add(new TableTransactionAction(TableTransactionActionType.Add, folderEntity));
            bytes += ActionBytes(folderEntity);
        }

        return new ActionGroup(group.ItemId, actions, bytes);
    }

    private static void AddRealSecret(
        Guid targetOwnerId,
        Secret secret,
        IReadOnlyList<SecretVersion> versions,
        List<TableTransactionAction> actions,
        ref int bytes)
    {
        var partitionKey = VaultKeys.PartitionKey(targetOwnerId);
        var secretEntity = VaultStorageMappingRegister.ToEntity(secret);
        RequireOwner(partitionKey, secretEntity.PartitionKey);
        actions.Add(new TableTransactionAction(TableTransactionActionType.Add, secretEntity));
        bytes += ActionBytes(secretEntity);

        foreach (var version in versions)
        {
            var versionEntity = VaultStorageMappingRegister.ToEntity(version);
            RequireOwner(partitionKey, versionEntity.PartitionKey);
            actions.Add(new TableTransactionAction(TableTransactionActionType.Add, versionEntity));
            bytes += ActionBytes(versionEntity);
        }
    }

    private static TableTransactionAction DeleteAction(string partitionKey, Guid transferId, StagedItemRef item) =>
        new(
            TableTransactionActionType.Delete,
            new TableEntity(partitionKey, StagingRows.RowKeyOf(transferId, item)),
            new ETag(item.ETag));

    private static int ActionBytes(ITableEntity entity) =>
        VaultStorageLimits.ActionOverheadBytes + (entity is IBinaryPayload payload ? (int)(((long)payload.BinaryLength * 4 + 2) / 3) : 0);

    private static string RangeFilter(Guid ownerId, string rangeStart) =>
        TableClient.CreateQueryFilter(
            $"PartitionKey eq {VaultKeys.PartitionKey(ownerId)} and RowKey ge {rangeStart} and RowKey lt {VaultKeys.UpperBound(rangeStart)}");

    private static void RequireOwner(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new ArgumentException(OwnerMismatchMessage);
        }
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

    private static StoreWriteResult? MapFailure(int status) => status switch
    {
        PreconditionFailedStatus => StoreWriteResult.Conflict,
        NotFoundStatus => StoreWriteResult.NotFound,
        ConflictStatus => StoreWriteResult.AlreadyExists,
        _ => null,
    };
}
