// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Transfers;

internal sealed class TransferJobTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string State { get; set; } = string.Empty;

    public string SourceUserId { get; set; } = string.Empty;

    public string TargetUserId { get; set; } = string.Empty;

    public string SourceDisplayName { get; set; } = string.Empty;

    public string TargetDisplayName { get; set; } = string.Empty;

    public string ScopeKind { get; set; } = string.Empty;

    public string SelectedFolderIds { get; set; } = "[]";

    public string Category { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public string RequestedByName { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public string? DecidedBy { get; set; }

    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionReason { get; set; }

    public string? ApprovedFromIp { get; set; }

    public bool ApprovalAudited { get; set; }

    public string? RootFolderId { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? ReleaseStartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? BlockReason { get; set; }

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
