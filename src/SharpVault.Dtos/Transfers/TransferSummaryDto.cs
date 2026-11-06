// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Users;

namespace SharpVault.Dtos.Transfers;

public sealed class TransferSummaryDto
{
    public Guid Id { get; set; }

    public TransferStateDto State { get; set; }

    public string SourceDisplayName { get; set; } = string.Empty;

    public string TargetDisplayName { get; set; } = string.Empty;

    public ReasonCategoryDto Category { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public string RequestedByName { get; set; } = string.Empty;
}
