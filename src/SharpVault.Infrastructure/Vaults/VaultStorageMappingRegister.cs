// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;

namespace SharpVault.Infrastructure.Vaults;

public sealed class VaultStorageMappingRegister : IRegister
{
    private const string IdFormat = "N";
    private const string OwnerFormat = "D";

    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<Secret, SecretTableEntity>().MapWith(s => ToEntity(s));
        config.NewConfig<SecretTableEntity, Secret>().MapWith(e => ToDomain(e));
        config.NewConfig<Folder, FolderTableEntity>().MapWith(s => ToEntity(s));
        config.NewConfig<FolderTableEntity, Folder>().MapWith(e => ToDomain(e));
        config.NewConfig<SecretVersion, SecretVersionTableEntity>().MapWith(s => ToEntity(s));
        config.NewConfig<SecretVersionTableEntity, SecretVersion>().MapWith(e => ToDomain(e));
        config.NewConfig<VaultKeyRing, VaultKeyRingTableEntity>().MapWith(s => ToEntity(s));
        config.NewConfig<VaultKeyRingTableEntity, VaultKeyRing>().MapWith(e => ToDomain(e));
    }

    internal static SecretTableEntity ToEntity(Secret secret) => new()
    {
        PartitionKey = VaultKeys.PartitionKey(secret.OwnerId),
        RowKey = VaultKeys.SecretRowKey(secret.Id, secret.DeletedAt is not null),
        Type = secret.Type.ToString(),
        FolderKey = VaultKeys.FolderKey(secret.FolderId),
        NameEnvelope = secret.NameEnvelope,
        UsernameEnvelope = secret.UsernameEnvelope,
        UrlEnvelope = secret.UrlEnvelope,
        TagsEnvelope = secret.TagsEnvelope,
        ValueEnvelope = secret.ValueEnvelope,
        NotesEnvelope = secret.NotesEnvelope,
        DataKeyId = secret.DataKey.KeyId,
        DataKeyAlgorithm = secret.DataKey.Algorithm,
        WrappedDataKey = secret.DataKey.Value,
        MetadataKeyVersion = secret.MetadataKeyVersion,
        Revision = secret.Revision,
        CreatedAt = secret.CreatedAt,
        ModifiedAt = secret.ModifiedAt,
        DeletedAt = secret.DeletedAt,
        DeletionBatchId = secret.DeletionBatchId?.ToString(OwnerFormat),
    };

    internal static Secret ToDomain(SecretTableEntity entity) => new()
    {
        Id = ParseSuffix(entity.RowKey),
        OwnerId = Guid.ParseExact(entity.PartitionKey, OwnerFormat),
        Type = ParseType(entity.Type),
        FolderId = VaultKeys.ParseFolderKey(entity.FolderKey),
        NameEnvelope = entity.NameEnvelope ?? [],
        UsernameEnvelope = entity.UsernameEnvelope,
        UrlEnvelope = entity.UrlEnvelope,
        TagsEnvelope = entity.TagsEnvelope,
        ValueEnvelope = entity.ValueEnvelope,
        NotesEnvelope = entity.NotesEnvelope,
        DataKey = new WrappedDataKey(entity.DataKeyId, entity.DataKeyAlgorithm, entity.WrappedDataKey ?? []),
        MetadataKeyVersion = entity.MetadataKeyVersion,
        Revision = entity.Revision,
        CreatedAt = entity.CreatedAt,
        ModifiedAt = entity.ModifiedAt,
        DeletedAt = entity.DeletedAt,
        DeletionBatchId = ParseBatch(entity.DeletionBatchId),
    };

    internal static FolderTableEntity ToEntity(Folder folder) => new()
    {
        PartitionKey = VaultKeys.PartitionKey(folder.OwnerId),
        RowKey = VaultKeys.FolderRowKey(folder.Id, folder.DeletedAt is not null),
        ParentKey = VaultKeys.FolderKey(folder.ParentId),
        NameEnvelope = folder.NameEnvelope,
        MetadataKeyVersion = folder.MetadataKeyVersion,
        CreatedAt = folder.CreatedAt,
        ModifiedAt = folder.ModifiedAt,
        DeletedAt = folder.DeletedAt,
        DeletionBatchId = folder.DeletionBatchId?.ToString(OwnerFormat),
    };

    internal static Folder ToDomain(FolderTableEntity entity) => new()
    {
        Id = ParseSuffix(entity.RowKey),
        OwnerId = Guid.ParseExact(entity.PartitionKey, OwnerFormat),
        ParentId = VaultKeys.ParseFolderKey(entity.ParentKey),
        NameEnvelope = entity.NameEnvelope ?? [],
        MetadataKeyVersion = entity.MetadataKeyVersion,
        CreatedAt = entity.CreatedAt,
        ModifiedAt = entity.ModifiedAt,
        DeletedAt = entity.DeletedAt,
        DeletionBatchId = ParseBatch(entity.DeletionBatchId),
    };

    internal static SecretVersionTableEntity ToEntity(SecretVersion version) => new()
    {
        PartitionKey = VaultKeys.PartitionKey(version.OwnerId),
        RowKey = VaultKeys.VersionRowKey(version.SecretId, version.Id),
        SecretId = version.SecretId.ToString(OwnerFormat),
        ValueEnvelope = version.ValueEnvelope,
        Revision = version.Revision,
        ReplacedAt = version.ReplacedAt,
    };

    internal static SecretVersion ToDomain(SecretVersionTableEntity entity) => new()
    {
        Id = Guid.ParseExact(entity.RowKey[(entity.RowKey.LastIndexOf('_') + 1)..], IdFormat),
        SecretId = Guid.ParseExact(entity.SecretId, OwnerFormat),
        OwnerId = Guid.ParseExact(entity.PartitionKey, OwnerFormat),
        ValueEnvelope = entity.ValueEnvelope ?? [],
        Revision = entity.Revision,
        ReplacedAt = entity.ReplacedAt,
    };

    internal static VaultKeyRingTableEntity ToEntity(VaultKeyRing keyRing) => new()
    {
        PartitionKey = VaultKeys.PartitionKey(keyRing.OwnerId),
        RowKey = VaultKeys.KeyRingRowKey,
        DataKeyId = keyRing.MetadataKey.KeyId,
        DataKeyAlgorithm = keyRing.MetadataKey.Algorithm,
        WrappedDataKey = keyRing.MetadataKey.Value,
        MetadataKeyVersion = keyRing.MetadataKeyVersion,
        CreatedAt = keyRing.CreatedAt,
    };

    internal static VaultKeyRing ToDomain(VaultKeyRingTableEntity entity) => new()
    {
        OwnerId = Guid.ParseExact(entity.PartitionKey, OwnerFormat),
        MetadataKey = new WrappedDataKey(entity.DataKeyId, entity.DataKeyAlgorithm, entity.WrappedDataKey ?? []),
        MetadataKeyVersion = entity.MetadataKeyVersion,
        CreatedAt = entity.CreatedAt,
    };

    internal static ShareGrantTableEntity ToEntity(ShareGrant grant, bool byItem) => new()
    {
        PartitionKey = VaultKeys.PartitionKey(grant.OwnerId),
        RowKey = byItem
            ? VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId)
            : VaultKeys.GrantByRecipientRowKey(grant.RecipientId, grant.ItemKind, grant.ItemId),
        ItemKind = grant.ItemKind.ToString(),
        ItemId = grant.ItemId.ToString(OwnerFormat),
        RecipientId = grant.RecipientId.ToString(OwnerFormat),
        Permission = grant.Permission.ToString(),
        RecipientDisplayName = grant.RecipientDisplayName,
        RecipientEmail = grant.RecipientEmail,
        GrantedById = grant.GrantedById.ToString(OwnerFormat),
        GrantedByDisplayName = grant.GrantedByDisplayName,
        GrantedAt = grant.GrantedAt,
        ModifiedAt = grant.ModifiedAt,
    };

    internal static ShareGrant ToDomain(ShareGrantTableEntity entity) => new()
    {
        OwnerId = Guid.ParseExact(entity.PartitionKey, OwnerFormat),
        ItemKind = ParseItemKind(entity.ItemKind),
        ItemId = Guid.ParseExact(entity.ItemId, OwnerFormat),
        RecipientId = Guid.ParseExact(entity.RecipientId, OwnerFormat),
        Permission = ParsePermission(entity.Permission),
        RecipientDisplayName = entity.RecipientDisplayName,
        RecipientEmail = entity.RecipientEmail,
        GrantedById = Guid.ParseExact(entity.GrantedById, OwnerFormat),
        GrantedByDisplayName = entity.GrantedByDisplayName,
        GrantedAt = entity.GrantedAt,
        ModifiedAt = entity.ModifiedAt,
    };

    internal static ShareItemKind ParseItemKind(string value) => value switch
    {
        nameof(ShareItemKind.Secret) => ShareItemKind.Secret,
        nameof(ShareItemKind.Folder) => ShareItemKind.Folder,
        _ => throw new InvalidDataException("The stored item kind is not known."),
    };

    internal static SharePermission ParsePermission(string? value) => value switch
    {
        nameof(SharePermission.View) => SharePermission.View,
        nameof(SharePermission.Edit) => SharePermission.Edit,
        nameof(SharePermission.Manage) => SharePermission.Manage,
        _ => SharePermission.None,
    };

    private static Guid ParseSuffix(string rowKey) =>
        Guid.ParseExact(rowKey[(rowKey.IndexOf('_', StringComparison.Ordinal) + 1)..], IdFormat);

    private static Guid? ParseBatch(string? value) =>
        Guid.TryParseExact(value, OwnerFormat, out var id) ? id : null;

    private static SecretType ParseType(string value) =>
        Enum.TryParse<SecretType>(value, ignoreCase: false, out var type) && Enum.IsDefined(type) ? type : SecretType.Other;
}
