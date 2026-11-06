// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Maintenance;

internal sealed class MaintenanceLeaseTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Holder { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public string? RunId { get; set; }

    public string? AfterUserId { get; set; }

    public DateTimeOffset? RunStartedAt { get; set; }

    public DateTimeOffset? LastCompletedAt { get; set; }
}
