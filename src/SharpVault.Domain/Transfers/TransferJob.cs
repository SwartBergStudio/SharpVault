// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Users;

namespace SharpVault.Domain.Transfers;

public sealed class TransferJob
{
    public Guid Id { get; set; }

    public TransferState State { get; set; }

    public Guid SourceUserId { get; set; }

    public Guid TargetUserId { get; set; }

    public string SourceDisplayName { get; set; } = string.Empty;

    public string TargetDisplayName { get; set; } = string.Empty;

    public TransferScopeKind ScopeKind { get; set; }

    public IReadOnlyList<Guid> SelectedFolderIds { get; set; } = [];

    public ReasonCategory Category { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Guid RequestedBy { get; set; }

    public string RequestedByName { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? DecidedBy { get; set; }

    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionReason { get; set; }

    public string? ApprovedFromIp { get; set; }

    public bool ApprovalAudited { get; set; }

    public Guid? RootFolderId { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? ReleaseStartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public TransferBlockReason? BlockReason { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastFailureKind { get; set; }

    public bool CleanupPending { get; set; }

    public int ItemCount { get; set; }

    public int FolderCount { get; set; }

    public int VersionCount { get; set; }

    public int RecycledItemCount { get; set; }

    public int SkippedItemCount { get; set; }

    public int PublishedItemCount { get; set; }

    public int RenamedItemCount { get; set; }

    public DateTimeOffset? FailingSince { get; set; }
}
