// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Vaults;

internal sealed class ShareGrantTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string ItemKind { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string RecipientId { get; set; } = string.Empty;

    public string Permission { get; set; } = string.Empty;

    public string RecipientDisplayName { get; set; } = string.Empty;

    public string? RecipientEmail { get; set; }

    public string GrantedById { get; set; } = string.Empty;

    public string GrantedByDisplayName { get; set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }
}
