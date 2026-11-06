// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Users;

namespace SharpVault.Dtos.Transfers;

public sealed class TransferDto
{
    public Guid Id { get; set; }

    public TransferStateDto State { get; set; }

    public Guid SourceUserId { get; set; }

    public string SourceDisplayName { get; set; } = string.Empty;

    public Guid TargetUserId { get; set; }

    public string TargetDisplayName { get; set; } = string.Empty;

    public TransferScopeKindDto Scope { get; set; }

    public List<Guid> FolderIds { get; set; } = [];

    public ReasonCategoryDto Category { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Guid RequestedBy { get; set; }

    public string RequestedByName { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? DecidedBy { get; set; }

    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionReason { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public TransferBlockReasonDto? BlockReason { get; set; }

    public bool IsRetrying { get; set; }

    public int ItemCount { get; set; }

    public int FolderCount { get; set; }

    public int VersionCount { get; set; }

    public int RecycledItemCount { get; set; }

    public int SkippedItemCount { get; set; }

    public int PublishedItemCount { get; set; }

    public int RenamedItemCount { get; set; }

    public string? LastFailureKind { get; set; }

    public DateTimeOffset? FailingSince { get; set; }

    public bool CanApprove { get; set; }

    public bool CanReject { get; set; }

    public bool CanAbort { get; set; }

    public string ConcurrencyToken { get; set; } = string.Empty;
}
