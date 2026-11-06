// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.Serialization;
using Azure;
using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Vaults;

internal sealed class SecretTableEntity : ITableEntity, IBinaryPayload
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Type { get; set; } = string.Empty;

    public string FolderKey { get; set; } = VaultKeys.RootFolderKey;

    public byte[]? NameEnvelope { get; set; }

    public byte[]? UsernameEnvelope { get; set; }

    public byte[]? UrlEnvelope { get; set; }

    public byte[]? TagsEnvelope { get; set; }

    public byte[]? ValueEnvelope { get; set; }

    public byte[]? NotesEnvelope { get; set; }

    public string DataKeyId { get; set; } = string.Empty;

    public string DataKeyAlgorithm { get; set; } = string.Empty;

    public byte[]? WrappedDataKey { get; set; }

    public int MetadataKeyVersion { get; set; }

    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string? DeletionBatchId { get; set; }

    [IgnoreDataMember]
    public int BinaryLength =>
        BinaryLengths.Of(NameEnvelope, UsernameEnvelope, UrlEnvelope, TagsEnvelope, ValueEnvelope, NotesEnvelope, WrappedDataKey);

    public static SecretTableEntity From(TableEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new SecretTableEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            Type = entity.GetString(nameof(Type)) ?? string.Empty,
            FolderKey = entity.GetString(nameof(FolderKey)) ?? VaultKeys.RootFolderKey,
            NameEnvelope = entity.GetBinary(nameof(NameEnvelope)),
            UsernameEnvelope = entity.GetBinary(nameof(UsernameEnvelope)),
            UrlEnvelope = entity.GetBinary(nameof(UrlEnvelope)),
            TagsEnvelope = entity.GetBinary(nameof(TagsEnvelope)),
            ValueEnvelope = entity.GetBinary(nameof(ValueEnvelope)),
            NotesEnvelope = entity.GetBinary(nameof(NotesEnvelope)),
            DataKeyId = entity.GetString(nameof(DataKeyId)) ?? string.Empty,
            DataKeyAlgorithm = entity.GetString(nameof(DataKeyAlgorithm)) ?? string.Empty,
            WrappedDataKey = entity.GetBinary(nameof(WrappedDataKey)),
            MetadataKeyVersion = entity.GetInt32(nameof(MetadataKeyVersion)) ?? 0,
            Revision = entity.GetInt64(nameof(Revision)) ?? 0,
            CreatedAt = entity.GetDateTimeOffset(nameof(CreatedAt)) ?? default,
            ModifiedAt = entity.GetDateTimeOffset(nameof(ModifiedAt)) ?? default,
            DeletedAt = entity.GetDateTimeOffset(nameof(DeletedAt)),
            DeletionBatchId = entity.GetString(nameof(DeletionBatchId)),
        };
    }
}
