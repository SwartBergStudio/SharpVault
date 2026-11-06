// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferPreflightPhase(
    TransferGate gate,
    VaultKeyRingService keyRings,
    AdminAuditWriter audit,
    TimeProvider timeProvider)
{
    public async Task RunAsync(TransferJobCursor cursor, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        if (!job.ApprovalAudited)
        {
            await audit.WriteAsync(TransferAuditItems.Approved(job), cancellationToken);
            job.ApprovalAudited = true;
        }

        if (await gate.CheckAsync(job, checkFolderLimit: false, cancellationToken) is { } reason)
        {
            await BlockAsync(cursor, reason, cancellationToken);
            return;
        }

        using var sourceRead = await keyRings.OpenForReadAsync(job.SourceUserId, cancellationToken);
        if (sourceRead.State == VaultReadState.Unreadable)
        {
            await BlockAsync(cursor, TransferBlockReason.SourceVaultUnreadable, cancellationToken);
            return;
        }

        if (sourceRead.State == VaultReadState.NoRing)
        {
            await StartAsync(cursor, TransferState.Reassigning, cancellationToken);
            return;
        }

        if (!await CanOpenTargetAsync(job.TargetUserId, cancellationToken))
        {
            await BlockAsync(cursor, TransferBlockReason.TargetVaultUnreadable, cancellationToken);
            return;
        }

        await StartAsync(cursor, TransferState.Staging, cancellationToken);
    }

    public async Task ResumeAsync(TransferJobCursor cursor, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        if (await gate.CheckAsync(job, checkFolderLimit: job.StartedAt is not null, cancellationToken) is { } reason)
        {
            await BlockAsync(cursor, reason, cancellationToken);
            return;
        }

        if (await UnreadableKeyReasonAsync(job, cancellationToken) is { } keyReason)
        {
            await BlockAsync(cursor, keyReason, cancellationToken);
            return;
        }

        await audit.WriteAsync(TransferAuditItems.Resumed(job), cancellationToken);
        job.State = job.StartedAt is null ? TransferState.Approved : TransferState.Staging;
        job.BlockReason = null;
        job.NextAttemptAt = null;
        await cursor.SaveAsync(cancellationToken);
    }

    public async Task BlockAsync(TransferJobCursor cursor, TransferBlockReason reason, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        if (job.BlockReason != reason)
        {
            await audit.WriteAsync(TransferAuditItems.Blocked(job, reason), cancellationToken);
        }

        job.State = TransferState.Blocked;
        job.BlockReason = reason;
        job.NextAttemptAt = timeProvider.GetUtcNow() + TransferLimits.BlockedRetryDelay;
        await cursor.SaveAsync(cancellationToken);
    }

    private async Task<TransferBlockReason?> UnreadableKeyReasonAsync(TransferJob job, CancellationToken cancellationToken)
    {
        using var sourceRead = await keyRings.OpenForReadAsync(job.SourceUserId, cancellationToken);
        if (sourceRead.State == VaultReadState.Unreadable)
        {
            return TransferBlockReason.SourceVaultUnreadable;
        }

        if (sourceRead.State == VaultReadState.NoRing)
        {
            return null;
        }

        return await CanOpenTargetAsync(job.TargetUserId, cancellationToken) ? null : TransferBlockReason.TargetVaultUnreadable;
    }

    private async Task<bool> CanOpenTargetAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        try
        {
            using var target = await keyRings.OpenOrCreateAsync(targetUserId, cancellationToken);
            return true;
        }
        catch (SecretDecryptionException)
        {
            return false;
        }
    }

    private async Task StartAsync(TransferJobCursor cursor, TransferState next, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        await audit.WriteAsync(TransferAuditItems.Started(job), cancellationToken);
        job.StartedAt = timeProvider.GetUtcNow();
        job.State = next;
        job.BlockReason = null;
        job.NextAttemptAt = null;
        await cursor.SaveAsync(cancellationToken);
    }
}
