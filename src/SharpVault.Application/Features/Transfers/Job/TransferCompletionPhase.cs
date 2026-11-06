// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferCompletionPhase(
    TransferReassignPhase reassign,
    ITransferStagingStore staging,
    ITransferJobStore jobs,
    AdminAuditWriter audit,
    TimeProvider timeProvider)
{
    public async Task RunAsync(TransferJobCursor cursor, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        if (!await reassign.RunAsync(job, lease, cancellationToken))
        {
            throw new TransferStepPendingException();
        }

        await RemoveSkippedMarkersAsync(job, cancellationToken);
        var fence = await staging.TakeFenceAsync(job.TargetUserId, job.Id, cancellationToken);
        await staging.ReleaseFenceAsync(job.TargetUserId, job.Id, fence, cancellationToken);
        await UpsertNoticesAsync(job, cancellationToken);
        await audit.WriteAsync(TransferAuditItems.Completed(job), cancellationToken);

        job.State = TransferState.Completed;
        job.CompletedAt = timeProvider.GetUtcNow();
        await cursor.CloseAsync(cancellationToken);
    }

    private async Task RemoveSkippedMarkersAsync(TransferJob job, CancellationToken cancellationToken)
    {
        var staged = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
        var skipped = staged.Where(reference => reference.Kind == StagedItemKind.Skipped).ToList();
        if (skipped.Count > 0)
        {
            await staging.RemoveAsync(job.TargetUserId, job.Id, skipped, cancellationToken);
        }
    }

    private async Task UpsertNoticesAsync(TransferJob job, CancellationToken cancellationToken)
    {
        var completedAt = timeProvider.GetUtcNow();
        if (job.ItemCount + job.RecycledItemCount + job.FolderCount > 0)
        {
            await jobs.UpsertNoticeAsync(Notice(job, job.TargetUserId, TransferNoticeKind.Received, completedAt, job.RootFolderId), cancellationToken);
        }

        await jobs.UpsertNoticeAsync(Notice(job, job.RequestedBy, TransferNoticeKind.RequestCompleted, completedAt, null), cancellationToken);
        if (job.DecidedBy is { } approver)
        {
            await jobs.UpsertNoticeAsync(Notice(job, approver, TransferNoticeKind.ApprovalCompleted, completedAt, null), cancellationToken);
        }
    }

    private static TransferNotice Notice(TransferJob job, Guid userId, TransferNoticeKind kind, DateTimeOffset completedAt, Guid? folderId) => new()
    {
        UserId = userId,
        TransferId = job.Id,
        Kind = kind,
        SourceDisplayName = job.SourceDisplayName,
        CompletedAt = completedAt,
        ItemCount = job.ItemCount + job.RecycledItemCount - job.SkippedItemCount,
        FolderId = folderId,
    };
}
