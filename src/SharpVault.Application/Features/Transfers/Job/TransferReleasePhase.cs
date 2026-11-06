// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferReleasePhase(
    IVaultStore vaults,
    ITransferStagingStore staging,
    VaultKeyRingService keyRings,
    TransferStagePhase stage)
{
    private const int KeyRingTouchActions = 1;
    private const int PackChangeLimit = TransferLimits.MaxTransactionActions - KeyRingTouchActions;

    public async Task RunAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < TransferLimits.MaxReleasePasses; pass++)
        {
            await lease.RenewIfDueAsync(cancellationToken);
            await stage.StagePassAsync(cursor, lease, removeMissing: false, cancellationToken);
            if (await ReleasePassAsync(cursor, lease, cancellationToken))
            {
                cursor.Job.State = TransferState.Publishing;
                await cursor.SaveAsync(cancellationToken);
                return;
            }
        }

        throw new TransferStepPendingException();
    }

    private async Task<bool> ReleasePassAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        var rootId = TransferRootFolder.IdOf(job);
        using var sourceRead = await keyRings.OpenForReadAsync(job.SourceUserId, cancellationToken);
        if (sourceRead.State == VaultReadState.NoRing)
        {
            return true;
        }

        if (sourceRead.Vault is not { } source)
        {
            throw new TransferStepPendingException();
        }

        var index = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
        var before = await TransferInventoryReader.ReadAsync(vaults, job.SourceUserId, int.MaxValue, cancellationToken);
        var inScopeIds = TransferScope.Build(job, before.Rows, rootId).InScope.Select(row => row.ItemId).ToHashSet();
        await cursor.SaveAsync(cancellationToken);
        var keyRingETag = await ReleaseSecretsAsync(job, source, before.Rows, index, inScopeIds, lease, cancellationToken);
        if (keyRingETag is null)
        {
            return false;
        }

        var afterSecrets = await TransferInventoryReader.ReadAsync(vaults, job.SourceUserId, int.MaxValue, cancellationToken);
        var skipped = SkippedIds(index);
        var scope = TransferScope.Build(job, afterSecrets.Rows, rootId);
        if (scope.InScope.Any(row => TransferScope.IsSecret(row.Kind) && !skipped.Contains(row.ItemId)))
        {
            return false;
        }

        await cursor.SaveAsync(cancellationToken);
        await ReleaseFoldersAsync(job, scope, afterSecrets.Rows, index, keyRingETag, lease, cancellationToken);

        var finalRead = await TransferInventoryReader.ReadAsync(vaults, job.SourceUserId, int.MaxValue, cancellationToken);
        return IsFinished(TransferScope.Build(job, finalRead.Rows, rootId), finalRead.Rows, index, rootId);
    }

    private async Task<string?> ReleaseSecretsAsync(
        TransferJob job,
        OpenedVault source,
        IReadOnlyList<VaultInventoryRow> rows,
        IReadOnlyList<StagedItemRef> index,
        HashSet<Guid> inScopeIds,
        TransferLeaseKeeper lease,
        CancellationToken cancellationToken)
    {
        var secretRows = rows.Where(row => TransferScope.IsSecret(row.Kind)).ToDictionary(row => row.ItemId);
        var containers = rows
            .Where(row => row.Kind == VaultItemKind.Version && row.ContainerId is not null)
            .Select(row => row.ContainerId!.Value)
            .ToHashSet();

        var groups = new List<ReleaseGroup>();
        foreach (var reference in index.Where(reference => reference.Kind == StagedItemKind.Secret))
        {
            await lease.RenewIfDueAsync(cancellationToken);
            if (!inScopeIds.Contains(reference.ItemId) || !secretRows.TryGetValue(reference.ItemId, out var row) || row.ETag != reference.SourceETag)
            {
                continue;
            }

            var changes = new List<VaultChange> { ReleaseChange(row) };
            if (containers.Contains(row.ItemId))
            {
                var versions = await vaults.ListVersionsAsync(job.SourceUserId, row.ItemId, cancellationToken);
                changes.AddRange(versions.Select(version => VaultChange.DeleteVersion(version.Value, version.ETag)));
            }

            groups.Add(new ReleaseGroup(row.ItemId, changes));
        }

        var keyRingETag = source.KeyRingETag;
        foreach (var pack in Pack(groups))
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var nextKeyRingETag = await SubmitPackAsync(job.SourceUserId, keyRingETag, pack, lease, cancellationToken);
            if (nextKeyRingETag is null)
            {
                return null;
            }

            keyRingETag = nextKeyRingETag;
        }

        return keyRingETag;
    }

    private async Task<string?> SubmitPackAsync(
        Guid sourceUserId,
        string keyRingETag,
        IReadOnlyList<ReleaseGroup> pack,
        TransferLeaseKeeper lease,
        CancellationToken cancellationToken)
    {
        var write = await SubmitFencedAsync(sourceUserId, keyRingETag, pack.SelectMany(group => group.Changes), cancellationToken);
        if (write.Result == StoreWriteResult.Written)
        {
            return VaultWrites.ETagAt(write, 0);
        }

        var current = await vaults.GetKeyRingAsync(sourceUserId, cancellationToken);
        if (current is null || current.ETag != keyRingETag)
        {
            return null;
        }

        var ringETag = keyRingETag;
        foreach (var group in pack)
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var single = await SubmitFencedAsync(sourceUserId, ringETag, group.Changes, cancellationToken);
            if (single.Result == StoreWriteResult.Written)
            {
                ringETag = VaultWrites.ETagAt(single, 0);
            }
        }

        return ringETag;
    }

    private async Task<VaultWriteResult> SubmitFencedAsync(Guid sourceUserId, string keyRingETag, IEnumerable<VaultChange> changes, CancellationToken cancellationToken)
    {
        var write = await vaults.SubmitAsync(sourceUserId, [VaultChange.TouchKeyRing(keyRingETag), .. changes], cancellationToken);
        write.Interruption?.Throw();
        return write;
    }

    private async Task ReleaseFoldersAsync(
        TransferJob job,
        TransferScope scope,
        IReadOnlyList<VaultInventoryRow> rows,
        IReadOnlyList<StagedItemRef> index,
        string keyRingETag,
        TransferLeaseKeeper lease,
        CancellationToken cancellationToken)
    {
        var folders = TransferScope.FoldersDeepestFirst(scope.InScope.Where(TransferScope.IsFolder).ToList(), rows);
        var inScopeIds = scope.InScope.Select(row => row.ItemId).ToHashSet();
        var remainingSecrets = rows.Where(row => TransferScope.IsSecret(row.Kind) && inScopeIds.Contains(row.ItemId)).ToList();
        var stagedFolderETags = index
            .Where(reference => reference.Kind == StagedItemKind.Folder)
            .ToDictionary(reference => reference.ItemId, reference => reference.SourceETag);
        var removed = new HashSet<Guid>();

        foreach (var folder in folders)
        {
            await lease.RenewIfDueAsync(cancellationToken);
            if (remainingSecrets.Any(secret => secret.ContainerId == folder.ItemId)
                || folders.Any(child => child.ContainerId == folder.ItemId && !removed.Contains(child.ItemId))
                || !IsStagedAsCurrent(folder, stagedFolderETags))
            {
                continue;
            }

            var changes = new List<VaultChange>
            {
                VaultChange.TouchKeyRing(keyRingETag),
                folder.Kind == VaultItemKind.ActiveFolder
                    ? VaultChange.RemoveFolder(folder.ItemId, folder.ETag)
                    : VaultChange.RemoveRecycledFolder(folder.ItemId, folder.ETag),
            };

            var write = await vaults.SubmitAsync(job.SourceUserId, changes, cancellationToken);
            write.Interruption?.Throw();
            if (write.Result == StoreWriteResult.Written)
            {
                removed.Add(folder.ItemId);
                keyRingETag = VaultWrites.ETagAt(write, 0);
            }
        }
    }

    private static bool IsStagedAsCurrent(VaultInventoryRow folder, Dictionary<Guid, string?> stagedFolderETags) =>
        stagedFolderETags.TryGetValue(folder.ItemId, out var stagedETag) && stagedETag == folder.ETag;

    private static bool HasStaleStagedReference(TransferScope scope, IReadOnlyList<VaultInventoryRow> rows, IReadOnlyList<StagedItemRef> index, Guid rootId)
    {
        var secretIds = rows.Where(row => TransferScope.IsSecret(row.Kind)).Select(row => row.ItemId).ToHashSet();
        var folders = rows.Where(TransferScope.IsFolder).ToDictionary(row => row.ItemId);

        return index.Any(reference => reference.Kind switch
        {
            StagedItemKind.Secret => secretIds.Contains(reference.ItemId),
            StagedItemKind.Folder when reference.ItemId != rootId => folders.ContainsKey(reference.ItemId) && !scope.Contains(reference.ItemId),
            _ => false,
        });
    }

    private static bool IsFinished(TransferScope scope, IReadOnlyList<VaultInventoryRow> rows, IReadOnlyList<StagedItemRef> index, Guid rootId)
    {
        var skipped = SkippedIds(index);
        if (HasStaleStagedReference(scope, rows, index, rootId))
        {
            return false;
        }

        var parents = rows.Where(TransferScope.IsFolder).ToDictionary(row => row.ItemId, row => row.ContainerId);
        var keep = new HashSet<Guid>();
        foreach (var secret in rows.Where(row => TransferScope.IsSecret(row.Kind) && skipped.Contains(row.ItemId)))
        {
            Guid? cursor = secret.ContainerId;
            while (cursor is { } folderId && keep.Add(folderId) && parents.TryGetValue(folderId, out var parent))
            {
                cursor = parent;
            }
        }

        return scope.InScope.All(row => row.Kind switch
        {
            VaultItemKind.ActiveSecret or VaultItemKind.RecycledSecret => skipped.Contains(row.ItemId),
            VaultItemKind.Version => row.ContainerId is { } secretId && skipped.Contains(secretId),
            VaultItemKind.ActiveFolder or VaultItemKind.RecycledFolder => keep.Contains(row.ItemId),
            _ => true,
        });
    }

    private static HashSet<Guid> SkippedIds(IReadOnlyList<StagedItemRef> index) =>
        index.Where(reference => reference.Kind == StagedItemKind.Skipped).Select(reference => reference.ItemId).ToHashSet();

    private static VaultChange ReleaseChange(VaultInventoryRow row) => row.Kind == VaultItemKind.ActiveSecret
        ? VaultChange.RemoveSecret(row.ItemId, row.ETag)
        : VaultChange.RemoveRecycledSecret(row.ItemId, row.ETag);

    private static List<List<ReleaseGroup>> Pack(IReadOnlyList<ReleaseGroup> groups)
    {
        var packs = new List<List<ReleaseGroup>>();
        var current = new List<ReleaseGroup>();
        var actions = 0;
        foreach (var group in groups)
        {
            if (actions + group.Changes.Count > PackChangeLimit && current.Count > 0)
            {
                packs.Add(current);
                current = [];
                actions = 0;
            }

            current.Add(group);
            actions += group.Changes.Count;
        }

        if (current.Count > 0)
        {
            packs.Add(current);
        }

        return packs;
    }

    private sealed record ReleaseGroup(Guid ItemId, IReadOnlyList<VaultChange> Changes);
}
