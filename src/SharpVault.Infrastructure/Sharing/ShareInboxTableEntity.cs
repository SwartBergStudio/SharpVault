// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Sharing;

internal sealed class ShareInboxTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public string ItemKind { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public DateTimeOffset SharedAt { get; set; }

    public string? FiledFolderKey { get; set; }
}
