// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Transfers;

public sealed class TransferNotice
{
    public Guid UserId { get; set; }

    public Guid TransferId { get; set; }

    public TransferNoticeKind Kind { get; set; }

    public string SourceDisplayName { get; set; } = string.Empty;

    public DateTimeOffset CompletedAt { get; set; }

    public int ItemCount { get; set; }

    public Guid? FolderId { get; set; }
}
