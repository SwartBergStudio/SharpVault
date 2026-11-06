// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Transfers;

internal static class StagingRows
{
    public const string StagedKindColumn = "StagedKind";
    public const string StagedSourceETagColumn = "StagedSourceETag";
    public const string StagedVersionCountColumn = "StagedVersionCount";

    private const string GuidFormat = "N";
    private const int GuidTextLength = 32;

    public static TableEntity ToSecretRow(Guid transferId, StagedSecret item)
    {
        var entity = VaultStorageMappingRegister.ToEntity(item.Secret);
        var row = NewRow(entity.PartitionKey, VaultKeys.StagedSecretRowKey(transferId, item.Secret.Id), StagedItemKind.Secret, item.SourceETag);
        Put(row, nameof(SecretTableEntity.Type), entity.Type);
        Put(row, nameof(SecretTableEntity.FolderKey), entity.FolderKey);
        Put(row, nameof(SecretTableEntity.NameEnvelope), entity.NameEnvelope);
        Put(row, nameof(SecretTableEntity.UsernameEnvelope), entity.UsernameEnvelope);
        Put(row, nameof(SecretTableEntity.UrlEnvelope), entity.UrlEnvelope);
        Put(row, nameof(SecretTableEntity.TagsEnvelope), entity.TagsEnvelope);
        Put(row, nameof(SecretTableEntity.ValueEnvelope), entity.ValueEnvelope);
        Put(row, nameof(SecretTableEntity.NotesEnvelope), entity.NotesEnvelope);
        Put(row, nameof(SecretTableEntity.DataKeyId), entity.DataKeyId);
        Put(row, nameof(SecretTableEntity.DataKeyAlgorithm), entity.DataKeyAlgorithm);
        Put(row, nameof(SecretTableEntity.WrappedDataKey), entity.WrappedDataKey);
        Put(row, nameof(SecretTableEntity.MetadataKeyVersion), entity.MetadataKeyVersion);
        Put(row, nameof(SecretTableEntity.Revision), entity.Revision);
        Put(row, nameof(SecretTableEntity.CreatedAt), entity.CreatedAt);
        Put(row, nameof(SecretTableEntity.ModifiedAt), entity.ModifiedAt);
        Put(row, nameof(SecretTableEntity.DeletedAt), entity.DeletedAt);
        Put(row, nameof(SecretTableEntity.DeletionBatchId), entity.DeletionBatchId);
        Put(row, StagedVersionCountColumn, item.Versions.Count);
        return row;
    }

    public static TableEntity ToVersionRow(Guid transferId, SecretVersion version, string sourceETag)
    {
        var entity = VaultStorageMappingRegister.ToEntity(version);
        var row = NewRow(entity.PartitionKey, VaultKeys.StagedVersionRowKey(transferId, version.SecretId, version.Id), StagedItemKind.Version, sourceETag);
        Put(row, nameof(SecretVersionTableEntity.SecretId), entity.SecretId);
        Put(row, nameof(SecretVersionTableEntity.ValueEnvelope), entity.ValueEnvelope);
        Put(row, nameof(SecretVersionTableEntity.Revision), entity.Revision);
        Put(row, nameof(SecretVersionTableEntity.ReplacedAt), entity.ReplacedAt);
        return row;
    }

    public static TableEntity ToFolderRow(Guid transferId, StagedFolder item)
    {
        var entity = VaultStorageMappingRegister.ToEntity(item.Folder);
        var row = NewRow(entity.PartitionKey, VaultKeys.StagedFolderRowKey(transferId, item.Folder.Id), StagedItemKind.Folder, item.SourceETag);
        Put(row, nameof(FolderTableEntity.ParentKey), entity.ParentKey);
        Put(row, nameof(FolderTableEntity.NameEnvelope), entity.NameEnvelope);
        Put(row, nameof(FolderTableEntity.MetadataKeyVersion), entity.MetadataKeyVersion);
        Put(row, nameof(FolderTableEntity.CreatedAt), entity.CreatedAt);
        Put(row, nameof(FolderTableEntity.ModifiedAt), entity.ModifiedAt);
        Put(row, nameof(FolderTableEntity.DeletedAt), entity.DeletedAt);
        Put(row, nameof(FolderTableEntity.DeletionBatchId), entity.DeletionBatchId);
        return row;
    }

    public static TableEntity ToSkippedRow(string partitionKey, Guid transferId, Guid secretId, string sourceETag) =>
        NewRow(partitionKey, VaultKeys.StagedSkippedRowKey(transferId, secretId), StagedItemKind.Skipped, sourceETag);

    public static Secret ToSecret(TableEntity row)
    {
        var id = IdAfterCode(row.RowKey);
        row.RowKey = VaultKeys.SecretRowKey(id, recycled: row.GetDateTimeOffset(nameof(SecretTableEntity.DeletedAt)) is not null);
        return VaultStorageMappingRegister.ToDomain(SecretTableEntity.From(row));
    }

    public static Folder ToFolder(TableEntity row)
    {
        var id = IdAfterCode(row.RowKey);
        row.RowKey = VaultKeys.FolderRowKey(id, recycled: row.GetDateTimeOffset(nameof(FolderTableEntity.DeletedAt)) is not null);
        return VaultStorageMappingRegister.ToDomain(FolderTableEntity.From(row));
    }

    public static SecretVersion ToVersion(TableEntity row)
    {
        var (secretId, versionId) = VersionIdsOf(row.RowKey);
        row.RowKey = VaultKeys.VersionRowKey(secretId, versionId);
        return VaultStorageMappingRegister.ToDomain(SecretVersionTableEntity.From(row));
    }

    public static string RowKeyOf(Guid transferId, StagedItemRef item) => item.Kind switch
    {
        StagedItemKind.Folder => VaultKeys.StagedFolderRowKey(transferId, item.ItemId),
        StagedItemKind.Secret => VaultKeys.StagedSecretRowKey(transferId, item.ItemId),
        StagedItemKind.Skipped => VaultKeys.StagedSkippedRowKey(transferId, item.ItemId),
        StagedItemKind.Version => VaultKeys.StagedVersionRowKey(
            transferId,
            item.SecretId ?? throw new ArgumentException("A staged version reference needs its secret id."),
            item.ItemId),
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    public static StagedItemRef ToItemRef(Guid transferId, TableEntity row)
    {
        var tail = TailOf(transferId, row.RowKey);
        var code = tail[..VaultKeys.StagedFolderCode.Length];
        var ids = tail[VaultKeys.StagedFolderCode.Length..];
        var sourceETag = row.GetString(StagedSourceETagColumn);
        var versionCount = row.GetInt32(StagedVersionCountColumn) ?? 0;
        var etag = row.ETag.ToString();

        return code switch
        {
            VaultKeys.StagedFolderCode => new StagedItemRef(StagedItemKind.Folder, ParseId(ids), null, sourceETag, 0, etag),
            VaultKeys.StagedSecretCode => new StagedItemRef(StagedItemKind.Secret, ParseId(ids), null, sourceETag, versionCount, etag),
            VaultKeys.StagedSkippedCode => new StagedItemRef(StagedItemKind.Skipped, ParseId(ids), null, sourceETag, 0, etag),
            VaultKeys.StagedVersionCode => ToVersionRef(ids, sourceETag, etag),
            _ => throw new InvalidDataException("A staged row has an unknown key."),
        };
    }

    public static int VersionCountOf(TableEntity row) => row.GetInt32(StagedVersionCountColumn) ?? 0;

    public static string SourceETagOf(TableEntity row) => row.GetString(StagedSourceETagColumn) ?? string.Empty;

    private static StagedItemRef ToVersionRef(string ids, string? sourceETag, string etag)
    {
        var separator = ids.IndexOf('_', StringComparison.Ordinal);
        if (separator != GuidTextLength)
        {
            throw new InvalidDataException("A staged version row has an unknown key.");
        }

        return new StagedItemRef(StagedItemKind.Version, ParseId(ids[(separator + 1)..]), ParseId(ids[..separator]), sourceETag, 0, etag);
    }

    private static string TailOf(Guid transferId, string rowKey)
    {
        var start = VaultKeys.StagingRangeStart(transferId);
        if (!rowKey.StartsWith(start, StringComparison.Ordinal) || rowKey.Length < start.Length + VaultKeys.StagedFolderCode.Length)
        {
            throw new InvalidDataException("A staged row has an unknown key.");
        }

        return rowKey[start.Length..];
    }

    private static Guid IdAfterCode(string rowKey) => ParseId(rowKey[(rowKey.LastIndexOf('_') + 1)..]);

    private static (Guid SecretId, Guid VersionId) VersionIdsOf(string rowKey)
    {
        var last = rowKey.LastIndexOf('_');
        var previous = rowKey.LastIndexOf('_', last - 1);
        return (ParseId(rowKey[(previous + 1)..last]), ParseId(rowKey[(last + 1)..]));
    }

    private static Guid ParseId(string text) =>
        Guid.TryParseExact(text, GuidFormat, out var id) ? id : throw new InvalidDataException("A staged row key does not hold an id.");

    private static TableEntity NewRow(string partitionKey, string rowKey, StagedItemKind kind, string sourceETag)
    {
        var row = new TableEntity(partitionKey, rowKey);
        Put(row, StagedKindColumn, kind.ToString());
        Put(row, StagedSourceETagColumn, sourceETag);
        return row;
    }

    private static void Put(TableEntity row, string name, object? value)
    {
        if (value is not null)
        {
            row[name] = value;
        }
    }
}
