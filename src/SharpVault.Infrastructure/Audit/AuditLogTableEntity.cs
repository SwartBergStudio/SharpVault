// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Audit;

internal sealed class AuditLogTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string? TargetId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? IpAddress { get; set; }

    public string? Reason { get; set; }
}
