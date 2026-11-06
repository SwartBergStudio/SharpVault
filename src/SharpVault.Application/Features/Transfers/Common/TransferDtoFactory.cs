// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Transfers;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferDtoFactory
{
    public static TransferDto ToDto(Versioned<TransferJob> versioned, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(versioned);

        var job = versioned.Value;
        var isPending = job.State == TransferState.PendingApproval;
        var isRetrying = job.Attempts > 0 && (!TransferRules.IsFinal(job.State) || job.CleanupPending);

        return new TransferDto
        {
            Id = job.Id,
            State = TransferEnumMap.ToDto(job.State),
            SourceUserId = job.SourceUserId,
            SourceDisplayName = job.SourceDisplayName,
            TargetUserId = job.TargetUserId,
            TargetDisplayName = job.TargetDisplayName,
            Scope = TransferEnumMap.ToDto(job.ScopeKind),
            FolderIds = [.. job.SelectedFolderIds],
            Category = TransferEnumMap.ToDto(job.Category),
            Reason = job.Reason,
            RequestedBy = job.RequestedBy,
            RequestedByName = job.RequestedByName,
            RequestedAt = job.RequestedAt,
            ExpiresAt = job.ExpiresAt,
            DecidedBy = job.DecidedBy,
            DecidedByName = job.DecidedByName,
            DecidedAt = job.DecidedAt,
            DecisionReason = job.DecisionReason,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            BlockReason = job.BlockReason is { } reason ? TransferEnumMap.ToDto(reason) : null,
            IsRetrying = isRetrying,
            ItemCount = job.ItemCount,
            FolderCount = job.FolderCount,
            VersionCount = job.VersionCount,
            RecycledItemCount = job.RecycledItemCount,
            SkippedItemCount = job.SkippedItemCount,
            PublishedItemCount = job.PublishedItemCount,
            RenamedItemCount = job.RenamedItemCount,
            LastFailureKind = job.LastFailureKind,
            FailingSince = isRetrying ? job.FailingSince : null,
            CanApprove = isPending && job.RequestedBy != actorId,
            CanReject = isPending,
            CanAbort = TransferRules.CanAbort(job.State),
            ConcurrencyToken = versioned.ETag,
        };
    }

    public static TransferSummaryDto ToSummary(TransferJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new TransferSummaryDto
        {
            Id = job.Id,
            State = TransferEnumMap.ToDto(job.State),
            SourceDisplayName = job.SourceDisplayName,
            TargetDisplayName = job.TargetDisplayName,
            Category = TransferEnumMap.ToDto(job.Category),
            RequestedAt = job.RequestedAt,
            ExpiresAt = job.ExpiresAt,
            RequestedByName = job.RequestedByName,
        };
    }

    public static TransferNoticeDto ToNotice(TransferNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return new TransferNoticeDto
        {
            TransferId = notice.TransferId,
            Kind = TransferEnumMap.ToDto(notice.Kind),
            SourceDisplayName = notice.SourceDisplayName,
            CompletedAt = notice.CompletedAt,
            ItemCount = notice.ItemCount,
            FolderId = notice.FolderId,
        };
    }
}
