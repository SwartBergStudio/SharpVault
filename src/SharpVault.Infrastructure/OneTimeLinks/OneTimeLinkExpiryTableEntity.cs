// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.OneTimeLinks;

internal sealed class OneTimeLinkExpiryTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string CreatorId { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
