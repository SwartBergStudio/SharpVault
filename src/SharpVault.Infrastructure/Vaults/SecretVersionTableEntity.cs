// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.Serialization;
using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Vaults;

internal sealed class SecretVersionTableEntity : ITableEntity, IBinaryPayload
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string SecretId { get; set; } = string.Empty;

    public byte[]? ValueEnvelope { get; set; }

    public long Revision { get; set; }

    public DateTimeOffset ReplacedAt { get; set; }

    [IgnoreDataMember]
    public int BinaryLength => BinaryLengths.Of(ValueEnvelope);

    public static SecretVersionTableEntity From(TableEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new SecretVersionTableEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            SecretId = entity.GetString(nameof(SecretId)) ?? string.Empty,
            ValueEnvelope = entity.GetBinary(nameof(ValueEnvelope)),
            Revision = entity.GetInt64(nameof(Revision)) ?? 0,
            ReplacedAt = entity.GetDateTimeOffset(nameof(ReplacedAt)) ?? default,
        };
    }
}
