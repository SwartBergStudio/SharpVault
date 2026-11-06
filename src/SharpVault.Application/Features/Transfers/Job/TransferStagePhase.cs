// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferStagePhase(
    IVaultStore vaults,
    ITransferStagingStore staging,
    IEnvelopeEncryptor encryptor,
    VaultKeyRingService keyRings,
    TransferGate gate,
    TransferPreflightPhase preflight,
    AdminAuditWriter audit,
    TimeProvider timeProvider)
{
    public async Task RunAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < TransferLimits.MaxStagePasses; pass++)
        {
            if (!await StagePassAsync(cursor, lease, removeMissing: true, cancellationToken))
            {
                break;
            }
        }

        if (await gate.CheckAsync(cursor.Job, checkFolderLimit: true, cancellationToken) is { } reason)
        {
            await preflight.BlockAsync(cursor, reason, cancellationToken);
            return;
        }

        await BeginReleaseAsync(cursor, cancellationToken);
    }

    public async Task<bool> StagePassAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, bool removeMissing, CancellationToken cancellationToken)
    {
        await lease.RenewIfDueAsync(cancellationToken);
        var job = cursor.Job;
        var rootId = TransferRootFolder.IdOf(job);
        using var sourceRead = await keyRings.OpenForReadAsync(job.SourceUserId, cancellationToken);
        if (sourceRead.State == VaultReadState.NoRing)
        {
            return false;
        }

        if (sourceRead.Vault is not { } source)
        {
            throw new TransferStepPendingException();
        }

        using var target = await keyRings.OpenOrCreateAsync(job.TargetUserId, cancellationToken);
        var fence = await staging.TakeFenceAsync(job.TargetUserId, job.Id, cancellationToken);
        await SaveOrReleaseAsync(cursor, fence, cancellationToken);
        var inventory = await TransferInventoryReader.ReadAsync(vaults, job.SourceUserId, int.MaxValue, cancellationToken);
        var index = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
        var scope = TransferScope.Build(job, inventory.Rows, rootId);
        var plan = TransferStagePlanner.Build(scope, inventory.Rows, index, rootId, removeMissing);

        var changed = await RemoveRefsAsync(cursor, plan.Removals, cancellationToken);
        changed |= await StageSecretsAsync(cursor, fence, scope, inventory.Rows, index, source, target, plan.Secrets, lease, cancellationToken);
        changed |= await StageFoldersAsync(cursor, fence, scope, plan.Folders, source, target, lease, cancellationToken);
        if (plan.StageRoot)
        {
            await cursor.SaveAsync(cancellationToken);
            await StageRootAsync(job, rootId, target, fence, cancellationToken);
            changed = true;
        }

        if (removeMissing)
        {
            await UpdateCountsAsync(cursor, scope, cancellationToken);
        }
        else if (changed)
        {
            await RefreshSkippedCountAsync(cursor, cancellationToken);
        }

        return changed;
    }

    private async Task SaveOrReleaseAsync(TransferJobCursor cursor, StageFence fence, CancellationToken cancellationToken)
    {
        try
        {
            await cursor.SaveAsync(cancellationToken);
        }
        catch (TransferJobChangedException)
        {
            await staging.ReleaseFenceAsync(cursor.Job.TargetUserId, cursor.Job.Id, fence, cancellationToken);
            throw;
        }
    }

    private async Task BeginReleaseAsync(TransferJobCursor cursor, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        job.State = TransferState.Releasing;
        job.ReleaseStartedAt = timeProvider.GetUtcNow();
        await audit.WriteAsync(TransferAuditItems.ReleaseStarted(job), cancellationToken);
        await cursor.SaveAsync(cancellationToken);
    }

    private async Task<bool> RemoveRefsAsync(TransferJobCursor cursor, IReadOnlyList<StagedItemRef> references, CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return false;
        }

        await cursor.SaveAsync(cancellationToken);
        await staging.RemoveAsync(cursor.Job.TargetUserId, cursor.Job.Id, references, cancellationToken);
        return true;
    }

    private async Task<bool> StageSecretsAsync(
        TransferJobCursor cursor,
        StageFence fence,
        TransferScope scope,
        IReadOnlyList<VaultInventoryRow> rows,
        IReadOnlyList<StagedItemRef> index,
        OpenedVault source,
        OpenedVault target,
        IReadOnlyList<VaultInventoryRow> toStage,
        TransferLeaseKeeper lease,
        CancellationToken cancellationToken)
    {
        if (toStage.Count == 0)
        {
            return false;
        }

        var job = cursor.Job;
        var versionCounts = rows
            .Where(row => row.Kind == VaultItemKind.Version && row.ContainerId is not null)
            .GroupBy(row => row.ContainerId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var secretRefs = index.Where(reference => reference.Kind == StagedItemKind.Secret).ToDictionary(reference => reference.ItemId);
        var skipRefs = index.Where(reference => reference.Kind == StagedItemKind.Skipped).ToDictionary(reference => reference.ItemId);
        var versionRefs = index
            .Where(reference => reference.Kind == StagedItemKind.Version && reference.SecretId is not null)
            .ToLookup(reference => reference.SecretId!.Value);

        foreach (var batch in toStage.Chunk(TransferLimits.ReadPageSize))
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var candidates = await ReadCandidatesAsync(job, scope, batch, versionCounts, cancellationToken);

            var staged = new List<StagedSecret>();
            var failed = new List<SecretCandidate>();
            foreach (var candidate in candidates)
            {
                if (!candidate.Failed && TryBindMetadata(source.Session, target, candidate))
                {
                    staged.Add(new StagedSecret(candidate.Target, candidate.Versions, StaleReferences(candidate, versionRefs), candidate.SourceETag));
                }
                else
                {
                    failed.Add(candidate);
                }
            }

            await cursor.SaveAsync(cancellationToken);
            var oversized = (await staging.StageSecretsAsync(job.Id, staged, fence, cancellationToken)).ToHashSet();
            var fitting = staged.Where(item => !oversized.Contains(item.Secret.Id)).ToList();
            var resolvedSkips = fitting
                .Where(item => skipRefs.ContainsKey(item.Secret.Id))
                .Select(item => skipRefs[item.Secret.Id])
                .ToList();

            foreach (var item in staged.Where(item => oversized.Contains(item.Secret.Id)))
            {
                await lease.RenewIfDueAsync(cancellationToken);
                await SkipAsync(cursor, fence, item.Secret.Id, item.SourceETag, secretRefs, skipRefs, versionRefs, cancellationToken);
            }

            foreach (var candidate in failed)
            {
                await lease.RenewIfDueAsync(cancellationToken);
                await SkipAsync(cursor, fence, candidate.SecretId, candidate.SourceETag, secretRefs, skipRefs, versionRefs, cancellationToken);
            }

            await RemoveRefsAsync(cursor, resolvedSkips, cancellationToken);
        }

        return true;
    }

    private async Task<IReadOnlyList<SecretCandidate>> ReadCandidatesAsync(
        TransferJob job,
        TransferScope scope,
        VaultInventoryRow[] batch,
        IReadOnlyDictionary<Guid, int> versionCounts,
        CancellationToken cancellationToken)
    {
        var results = new SecretCandidate?[batch.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = TransferLimits.ReencryptParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, batch.Length), options, async (index, token) =>
        {
            results[index] = await ReadCandidateAsync(job, scope, batch[index], versionCounts, token);
        });

        return results.OfType<SecretCandidate>().ToList();
    }

    private async Task<SecretCandidate?> ReadCandidateAsync(
        TransferJob job,
        TransferScope scope,
        VaultInventoryRow row,
        IReadOnlyDictionary<Guid, int> versionCounts,
        CancellationToken cancellationToken)
    {
        var read = row.Kind == VaultItemKind.ActiveSecret
            ? await vaults.GetSecretAsync(job.SourceUserId, row.ItemId, cancellationToken)
            : await vaults.GetRecycledSecretAsync(job.SourceUserId, row.ItemId, cancellationToken);
        if (read is null)
        {
            return null;
        }

        var source = read.Value;
        var target = TransferCipher.Skeleton(source, job.TargetUserId, scope.MapFolderId(source.FolderId));
        var versions = versionCounts.ContainsKey(row.ItemId)
            ? (await vaults.ListVersionsAsync(job.SourceUserId, row.ItemId, cancellationToken)).Select(version => version.Value).ToList()
            : new List<SecretVersion>();
        var sourceVersionIds = versions.Select(version => version.Id).ToHashSet();

        try
        {
            var rebound = (IReadOnlyList<SecretVersion>)versions;
            if (HasContent(source, versions))
            {
                using var session = await encryptor.OpenDataKeyAsync(source.DataKey, cancellationToken);
                rebound = TransferCipher.RebindContent(session, source, target, versions);
            }

            return new SecretCandidate(source.Id, read.ETag, source, target, rebound, sourceVersionIds, false);
        }
        catch (SecretDecryptionException)
        {
            return new SecretCandidate(source.Id, read.ETag, source, target, Array.Empty<SecretVersion>(), sourceVersionIds, true);
        }
    }

    private static bool HasContent(Secret secret, List<SecretVersion> versions) =>
        secret.ValueEnvelope is not null || secret.NotesEnvelope is not null || versions.Count > 0;

    private static bool TryBindMetadata(IDataKeySession sourceSession, OpenedVault target, SecretCandidate candidate)
    {
        try
        {
            TransferCipher.RebindMetadata(sourceSession, target.Session, candidate.Source, candidate.Target, target.MetadataKeyVersion);
            return true;
        }
        catch (SecretDecryptionException)
        {
            return false;
        }
    }

    private static List<StagedItemRef> StaleReferences(SecretCandidate candidate, ILookup<Guid, StagedItemRef> versionRefs) =>
        versionRefs[candidate.SecretId]
            .Where(reference => !candidate.SourceVersionIds.Contains(reference.ItemId))
            .ToList();

    private async Task SkipAsync(
        TransferJobCursor cursor,
        StageFence fence,
        Guid secretId,
        string sourceETag,
        Dictionary<Guid, StagedItemRef> secretRefs,
        Dictionary<Guid, StagedItemRef> skipRefs,
        ILookup<Guid, StagedItemRef> versionRefs,
        CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        var earlier = new List<StagedItemRef>();
        if (secretRefs.TryGetValue(secretId, out var secretRef))
        {
            earlier.Add(secretRef);
        }

        earlier.AddRange(versionRefs[secretId]);

        if (!skipRefs.ContainsKey(secretId))
        {
            await audit.WriteAsync(TransferAuditItems.Skipped(job, secretId), cancellationToken);
        }

        await RemoveRefsAsync(cursor, earlier, cancellationToken);
        await staging.StageSkippedAsync(job.TargetUserId, job.Id, secretId, sourceETag, fence, cancellationToken);
    }

    private async Task<bool> StageFoldersAsync(
        TransferJobCursor cursor,
        StageFence fence,
        TransferScope scope,
        IReadOnlyList<VaultInventoryRow> folders,
        OpenedVault source,
        OpenedVault target,
        TransferLeaseKeeper lease,
        CancellationToken cancellationToken)
    {
        if (folders.Count == 0)
        {
            return false;
        }

        var job = cursor.Job;
        foreach (var batch in folders.Chunk(TransferLimits.ReadPageSize))
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var reads = await ReadFoldersAsync(job, batch, cancellationToken);
            var staged = new List<StagedFolder>();
            foreach (var read in reads.OfType<FolderRead>())
            {
                var moved = TransferCipher.RebindFolder(
                    source.Session,
                    target.Session,
                    read.Folder.Value,
                    job.TargetUserId,
                    scope.MapFolderId(read.Folder.Value.ParentId),
                    target.MetadataKeyVersion);
                staged.Add(new StagedFolder(moved, read.Folder.ETag));
            }

            if (staged.Count > 0)
            {
                await cursor.SaveAsync(cancellationToken);
                await staging.StageFoldersAsync(job.Id, staged, fence, cancellationToken);
            }
        }

        return true;
    }

    private async Task<IReadOnlyList<FolderRead?>> ReadFoldersAsync(TransferJob job, VaultInventoryRow[] batch, CancellationToken cancellationToken)
    {
        var results = new FolderRead?[batch.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = VaultLimits.FolderQueryParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, batch.Length), options, async (index, token) =>
        {
            var row = batch[index];
            var read = row.Kind == VaultItemKind.ActiveFolder
                ? await vaults.GetFolderAsync(job.SourceUserId, row.ItemId, token)
                : await vaults.GetRecycledFolderAsync(job.SourceUserId, row.ItemId, token);
            results[index] = read is null ? null : new FolderRead(read);
        });

        return results;
    }

    private async Task StageRootAsync(TransferJob job, Guid rootId, OpenedVault target, StageFence fence, CancellationToken cancellationToken)
    {
        var root = TransferCipher.BuildRootFolder(target.Session, job, rootId, target.MetadataKeyVersion, timeProvider.GetUtcNow());
        await staging.StageFoldersAsync(job.Id, [new StagedFolder(root, TransferRootFolder.RootSourceETag)], fence, cancellationToken);
    }

    private async Task UpdateCountsAsync(TransferJobCursor cursor, TransferScope scope, CancellationToken cancellationToken)
    {
        scope.ApplyCountsTo(cursor.Job);
        await RefreshSkippedCountAsync(cursor, cancellationToken);
    }

    private async Task RefreshSkippedCountAsync(TransferJobCursor cursor, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        var staged = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
        job.SkippedItemCount = staged.Count(reference => reference.Kind == StagedItemKind.Skipped);
        await cursor.SaveAsync(cancellationToken);
    }

    private sealed record SecretCandidate(
        Guid SecretId,
        string SourceETag,
        Secret Source,
        Secret Target,
        IReadOnlyList<SecretVersion> Versions,
        IReadOnlySet<Guid> SourceVersionIds,
        bool Failed);

    private sealed record FolderRead(Versioned<Folder> Folder);
}
