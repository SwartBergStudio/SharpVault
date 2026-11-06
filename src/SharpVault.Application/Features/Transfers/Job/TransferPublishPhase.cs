// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed partial class TransferPublishPhase(
    ITransferStagingStore staging,
    IVaultStore vaults,
    VaultKeyRingService keyRings,
    IEnvelopeEncryptor encryptor,
    AdminAuditWriter audit,
    ILogger<TransferPublishPhase> logger)
{
    public async Task RunAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < TransferLimits.MaxPublishPasses; pass++)
        {
            await lease.RenewIfDueAsync(cancellationToken);
            await PublishFoldersAsync(cursor, lease, cancellationToken);
            await PublishSecretsAsync(cursor, lease, cancellationToken);

            var job = cursor.Job;
            var remaining = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
            if (!remaining.Any(reference => reference.Kind is StagedItemKind.Folder or StagedItemKind.Secret or StagedItemKind.Version))
            {
                job.State = TransferState.Reassigning;
                await cursor.SaveAsync(cancellationToken);
                return;
            }
        }

        throw new TransferStepPendingException();
    }

    private async Task PublishFoldersAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        var index = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
        var folderRefs = index.Where(reference => reference.Kind == StagedItemKind.Folder).ToList();
        if (folderRefs.Count == 0)
        {
            return;
        }

        var existing = (await vaults.ListFoldersAsync(job.TargetUserId, cancellationToken))
            .Select(folder => folder.Value.Id)
            .ToHashSet();
        var staged = await staging.ReadFoldersAsync(job.TargetUserId, job.Id, cancellationToken);
        var groups = new List<PublishGroup>();
        foreach (var item in staged)
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var refs = folderRefs.Where(reference => reference.ItemId == item.Folder.Id).ToList();
            if (refs.Count > 0)
            {
                groups.Add(await FolderGroupAsync(job, item.Folder, refs, existing.Contains(item.Folder.Id), cancellationToken));
            }
        }

        if (groups.Count == 0)
        {
            return;
        }

        await cursor.SaveAsync(cancellationToken);
        foreach (var group in groups.Where(group => !group.MergeOnly))
        {
            await lease.RenewIfDueAsync(cancellationToken);
            await audit.WriteAsync(TransferAuditItems.Transferred(job, AuditEventType.FolderTransferred, group.ItemId, isSecret: false), cancellationToken);
        }

        var failures = await staging.PublishAsync(job.TargetUserId, job.Id, groups, cancellationToken);
        LogFolderFailures(job.Id, failures);
    }

    private async Task<PublishGroup> FolderGroupAsync(TransferJob job, Folder folder, List<StagedItemRef> refs, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            return new PublishGroup(folder.Id, null, null, [], refs, MergeOnly: true);
        }

        var recycled = await vaults.GetRecycledFolderAsync(job.TargetUserId, folder.Id, cancellationToken);
        if (recycled is null)
        {
            return new PublishGroup(folder.Id, null, folder, [], refs, MergeOnly: false);
        }

        return folder.DeletedAt is null
            ? new PublishGroup(folder.Id, null, AsActive(folder), [], refs, false, recycled.ETag)
            : new PublishGroup(folder.Id, null, null, [], refs, MergeOnly: true);
    }

    private static Folder AsActive(Folder folder) => new()
    {
        Id = folder.Id,
        OwnerId = folder.OwnerId,
        ParentId = folder.ParentId,
        NameEnvelope = folder.NameEnvelope,
        MetadataKeyVersion = folder.MetadataKeyVersion,
        CreatedAt = folder.CreatedAt,
        ModifiedAt = folder.ModifiedAt,
    };

    private void LogFolderFailures(Guid transferId, IReadOnlyList<PublishFailure> failures)
    {
        foreach (var group in failures.GroupBy(failure => failure.Result))
        {
            LogFolderPublishFailure(logger, transferId, group.Key, group.Count());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transfer {TransferId} left {FailureCount} folders unpublished: {FailureType}")]
    private static partial void LogFolderPublishFailure(ILogger logger, Guid transferId, StoreWriteResult failureType, int failureCount);

    private async Task PublishSecretsAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        var index = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
        var secretRefs = index.Where(reference => reference.Kind == StagedItemKind.Secret).ToDictionary(reference => reference.ItemId);
        var versionRefs = index
            .Where(reference => reference.Kind == StagedItemKind.Version && reference.SecretId is not null)
            .ToLookup(reference => reference.SecretId!.Value);

        string? token = null;
        do
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var page = await staging.ReadSecretsAsync(job.TargetUserId, job.Id, TransferLimits.PublishPageSize, token, cancellationToken);
            var items = page.Items.Where(item => secretRefs.ContainsKey(item.Secret.Id)).ToList();
            if (items.Count > 0)
            {
                await cursor.SaveAsync(cancellationToken);
                await AuditTransferredAsync(job, items, cancellationToken);
                var groups = items.Select(item => new PublishGroup(
                    item.Secret.Id,
                    item.Secret,
                    null,
                    item.Versions,
                    [secretRefs[item.Secret.Id], .. versionRefs[item.Secret.Id]],
                    MergeOnly: false)).ToList();

                var failures = await staging.PublishAsync(job.TargetUserId, job.Id, groups, cancellationToken);
                job.PublishedItemCount += groups.Count - failures.Count;
                await cursor.SaveAsync(cancellationToken);

                var collided = failures
                    .Where(failure => failure.Result == StoreWriteResult.AlreadyExists)
                    .Select(failure => failure.ItemId)
                    .ToHashSet();
                if (collided.Count > 0)
                {
                    await PublishUnderNewIdsAsync(cursor, items.Where(item => collided.Contains(item.Secret.Id)).ToList(), secretRefs, versionRefs, lease, cancellationToken);
                }
            }

            token = page.ContinuationToken;
        }
        while (token is not null);
    }

    private async Task PublishUnderNewIdsAsync(
        TransferJobCursor cursor,
        IReadOnlyList<StagedSecret> collisions,
        Dictionary<Guid, StagedItemRef> secretRefs,
        ILookup<Guid, StagedItemRef> versionRefs,
        TransferLeaseKeeper lease,
        CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        using var target = await keyRings.OpenForReadAsync(job.TargetUserId, cancellationToken);
        if (target.Vault is not { } vault)
        {
            throw new TransferStepPendingException();
        }

        var rootId = TransferRootFolder.IdOf(job);
        var groups = new List<PublishGroup>();
        foreach (var item in collisions)
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var newSecretId = VaultIds.TransferredSecretIdFor(job.Id, item.Secret.Id);
            using var content = await encryptor.OpenDataKeyAsync(item.Secret.DataKey, cancellationToken);
            var renamed = TransferCipher.Rename(vault.Session, content, item.Secret, newSecretId, rootId, vault.MetadataKeyVersion, item.Versions);
            await audit.WriteAsync(TransferAuditItems.RenamedForTarget(job, item.Secret.Id, newSecretId), cancellationToken);
            LogSecretRenamed(logger, job.Id, item.Secret.Id, newSecretId);
            groups.Add(new PublishGroup(
                newSecretId,
                renamed.Secret,
                null,
                renamed.Versions,
                [secretRefs[item.Secret.Id], .. versionRefs[item.Secret.Id]],
                MergeOnly: false));
        }

        await cursor.SaveAsync(cancellationToken);
        var failures = await staging.PublishAsync(job.TargetUserId, job.Id, groups, cancellationToken);
        if (failures.Count > 0)
        {
            throw new TransferPublishCollisionException();
        }

        job.PublishedItemCount += groups.Count;
        job.RenamedItemCount += groups.Count;
        await cursor.SaveAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transfer {TransferId} published secret {SecretId} under new id {NewSecretId} because the target already holds that id")]
    private static partial void LogSecretRenamed(ILogger logger, Guid transferId, Guid secretId, Guid newSecretId);

    private async Task AuditTransferredAsync(TransferJob job, IReadOnlyList<StagedSecret> items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = VaultLimits.AuditParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(items, options, async (item, token) =>
        {
            await audit.WriteAsync(TransferAuditItems.Transferred(job, AuditEventType.SecretTransferred, item.Secret.Id, isSecret: true), token);
        });
    }
}
