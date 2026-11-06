// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.Serialization;
using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Vaults;

internal sealed class FolderTableEntity : ITableEntity, IBinaryPayload
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string ParentKey { get; set; } = VaultKeys.RootFolderKey;

    public byte[]? NameEnvelope { get; set; }

    public int MetadataKeyVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string? DeletionBatchId { get; set; }

    [IgnoreDataMember]
    public int BinaryLength => BinaryLengths.Of(NameEnvelope);

    public static FolderTableEntity From(TableEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new FolderTableEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            ParentKey = entity.GetString(nameof(ParentKey)) ?? VaultKeys.RootFolderKey,
            NameEnvelope = entity.GetBinary(nameof(NameEnvelope)),
            MetadataKeyVersion = entity.GetInt32(nameof(MetadataKeyVersion)) ?? 0,
            CreatedAt = entity.GetDateTimeOffset(nameof(CreatedAt)) ?? default,
            ModifiedAt = entity.GetDateTimeOffset(nameof(ModifiedAt)) ?? default,
            DeletedAt = entity.GetDateTimeOffset(nameof(DeletedAt)),
            DeletionBatchId = entity.GetString(nameof(DeletionBatchId)),
        };
    }
}
