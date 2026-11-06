// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

public sealed class VaultStorageMappingTests
{
    private static readonly string[] SecretColumns =
    [
        "BinaryLength", "CreatedAt", "DataKeyAlgorithm", "DataKeyId", "DeletedAt", "DeletionBatchId", "ETag", "FolderKey",
        "MetadataKeyVersion", "ModifiedAt", "NameEnvelope", "NotesEnvelope", "PartitionKey", "Revision", "RowKey", "TagsEnvelope",
        "Timestamp", "Type", "UrlEnvelope", "UsernameEnvelope", "ValueEnvelope", "WrappedDataKey",
    ];

    private static readonly string[] FolderColumns =
    [
        "BinaryLength", "CreatedAt", "DeletedAt", "DeletionBatchId", "ETag", "MetadataKeyVersion", "ModifiedAt", "NameEnvelope",
        "ParentKey", "PartitionKey", "RowKey", "Timestamp",
    ];

    private static readonly string[] VersionColumns =
    [
        "BinaryLength", "ETag", "PartitionKey", "ReplacedAt", "Revision", "RowKey", "SecretId", "Timestamp", "ValueEnvelope",
    ];

    private static readonly string[] KeyRingColumns =
    [
        "CreatedAt", "DataKeyAlgorithm", "DataKeyId", "ETag", "MetadataKeyVersion", "PartitionKey", "RowKey", "Timestamp", "WrappedDataKey",
    ];

    private static TypeAdapterConfig ScannedConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(VaultKeys).Assembly);
        return config;
    }

    [Fact]
    public void Secret_RoundTripsEveryColumn()
    {
        var config = ScannedConfig();
        var secret = VaultSamples.Secret(Guid.NewGuid(), folderId: Guid.NewGuid());
        secret.Type = SecretType.ApiKey;

        var entity = secret.Adapt<SecretTableEntity>(config);
        var back = entity.Adapt<Secret>(config);

        Assert.Equal(secret.OwnerId.ToString("D"), entity.PartitionKey);
        Assert.Equal("s_" + secret.Id.ToString("N"), entity.RowKey);
        Assert.Equal("ApiKey", entity.Type);
        Assert.Equal(secret.FolderId!.Value.ToString("N"), entity.FolderKey);
        Assert.Equal(secret.Id, back.Id);
        Assert.Equal(secret.OwnerId, back.OwnerId);
        Assert.Equal(secret.Type, back.Type);
        Assert.Equal(secret.FolderId, back.FolderId);
        Assert.Equal(secret.NameEnvelope, back.NameEnvelope);
        Assert.Equal(secret.UsernameEnvelope, back.UsernameEnvelope);
        Assert.Equal(secret.UrlEnvelope, back.UrlEnvelope);
        Assert.Equal(secret.TagsEnvelope, back.TagsEnvelope);
        Assert.Equal(secret.ValueEnvelope, back.ValueEnvelope);
        Assert.Equal(secret.NotesEnvelope, back.NotesEnvelope);
        Assert.Equal(secret.DataKey.KeyId, back.DataKey.KeyId);
        Assert.Equal(secret.DataKey.Algorithm, back.DataKey.Algorithm);
        Assert.Equal(secret.DataKey.Value, back.DataKey.Value);
        Assert.Equal(secret.MetadataKeyVersion, back.MetadataKeyVersion);
        Assert.Equal(secret.Revision, back.Revision);
        Assert.Equal(secret.CreatedAt, back.CreatedAt);
        Assert.Equal(secret.ModifiedAt, back.ModifiedAt);
        Assert.Null(back.DeletedAt);
        Assert.Null(back.DeletionBatchId);
    }

    [Fact]
    public void Secret_NullOptionalEnvelopes_StayNull()
    {
        var config = ScannedConfig();
        var secret = VaultSamples.Secret(Guid.NewGuid());
        secret.UsernameEnvelope = null;
        secret.UrlEnvelope = null;
        secret.TagsEnvelope = null;
        secret.ValueEnvelope = null;
        secret.NotesEnvelope = null;

        var back = secret.Adapt<SecretTableEntity>(config).Adapt<Secret>(config);

        Assert.Null(back.UsernameEnvelope);
        Assert.Null(back.UrlEnvelope);
        Assert.Null(back.TagsEnvelope);
        Assert.Null(back.ValueEnvelope);
        Assert.Null(back.NotesEnvelope);
        Assert.Equal(VaultKeys.RootFolderKey, secret.Adapt<SecretTableEntity>(config).FolderKey);
        Assert.Null(back.FolderId);
    }

    [Fact]
    public void Secret_Recycled_UsesBsPrefix()
    {
        var config = ScannedConfig();
        var batch = Guid.NewGuid();
        var secret = VaultSamples.Recycled(Guid.NewGuid(), batch: batch);

        var entity = secret.Adapt<SecretTableEntity>(config);
        var back = entity.Adapt<Secret>(config);

        Assert.Equal("bs_" + secret.Id.ToString("N"), entity.RowKey);
        Assert.Equal(batch.ToString("D"), entity.DeletionBatchId);
        Assert.Equal(secret.Id, back.Id);
        Assert.Equal(secret.DeletedAt, back.DeletedAt);
        Assert.Equal(batch, back.DeletionBatchId);
    }

    [Theory]
    [InlineData("Weird")]
    [InlineData("")]
    [InlineData("login")]
    [InlineData("99")]
    [InlineData("0")]
    public void Secret_UnknownTypeString_MapsToOther(string type)
    {
        var config = ScannedConfig();
        var entity = VaultSamples.Secret(Guid.NewGuid()).Adapt<SecretTableEntity>(config);
        entity.Type = type;

        var back = entity.Adapt<Secret>(config);

        Assert.Equal(SecretType.Other, back.Type);
    }

    [Fact]
    public void Secret_MissingNameEnvelopeAndWrappedKey_MapToEmptyArrays()
    {
        var config = ScannedConfig();
        var entity = VaultSamples.Secret(Guid.NewGuid()).Adapt<SecretTableEntity>(config);
        entity.NameEnvelope = null;
        entity.WrappedDataKey = null;

        var back = entity.Adapt<Secret>(config);

        Assert.Empty(back.NameEnvelope);
        Assert.Empty(back.DataKey.Value);
    }

    [Fact]
    public void Secret_ETagAndTimestamp_AreNotPartOfTheDomainModel()
    {
        var names = typeof(Secret).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("ETag", names);
        Assert.DoesNotContain("Timestamp", names);
        Assert.DoesNotContain("PartitionKey", names);
    }

    [Fact]
    public void Folder_RootParent_UsesRootKey()
    {
        var config = ScannedConfig();
        var folder = VaultSamples.Folder(Guid.NewGuid());

        var entity = folder.Adapt<FolderTableEntity>(config);
        var back = entity.Adapt<Folder>(config);

        Assert.Equal("root", entity.ParentKey);
        Assert.Equal("f_" + folder.Id.ToString("N"), entity.RowKey);
        Assert.Null(back.ParentId);
        Assert.Equal(folder.Id, back.Id);
        Assert.Equal(folder.OwnerId, back.OwnerId);
        Assert.Equal(folder.NameEnvelope, back.NameEnvelope);
        Assert.Equal(folder.MetadataKeyVersion, back.MetadataKeyVersion);
        Assert.Equal(folder.CreatedAt, back.CreatedAt);
        Assert.Equal(folder.ModifiedAt, back.ModifiedAt);
    }

    [Fact]
    public void Folder_WithParentAndRecycled_RoundTrips()
    {
        var config = ScannedConfig();
        var parent = Guid.NewGuid();
        var batch = Guid.NewGuid();
        var folder = VaultSamples.RecycledFolder(Guid.NewGuid(), parentId: parent, batch: batch);

        var entity = folder.Adapt<FolderTableEntity>(config);
        var back = entity.Adapt<Folder>(config);

        Assert.Equal("bf_" + folder.Id.ToString("N"), entity.RowKey);
        Assert.Equal(parent.ToString("N"), entity.ParentKey);
        Assert.Equal(parent, back.ParentId);
        Assert.Equal(batch, back.DeletionBatchId);
        Assert.Equal(folder.DeletedAt, back.DeletedAt);
    }

    [Fact]
    public void Version_RoundTripsAndKeepsSecretIdInTheRowKey()
    {
        var config = ScannedConfig();
        var owner = Guid.NewGuid();
        var version = VaultSamples.Version(owner, Guid.NewGuid());

        var entity = version.Adapt<SecretVersionTableEntity>(config);
        var back = entity.Adapt<SecretVersion>(config);

        Assert.Equal($"h_{version.SecretId:N}_{version.Id:N}", entity.RowKey);
        Assert.Equal(version.Id, back.Id);
        Assert.Equal(version.SecretId, back.SecretId);
        Assert.Equal(owner, back.OwnerId);
        Assert.Equal(version.ValueEnvelope, back.ValueEnvelope);
        Assert.Equal(version.Revision, back.Revision);
        Assert.Equal(version.ReplacedAt, back.ReplacedAt);
    }

    [Fact]
    public void KeyRing_RoundTripsWrappedKeyParts()
    {
        var config = ScannedConfig();
        var keyRing = VaultSamples.KeyRing(Guid.NewGuid());

        var entity = keyRing.Adapt<VaultKeyRingTableEntity>(config);
        var back = entity.Adapt<VaultKeyRing>(config);

        Assert.Equal(keyRing.OwnerId.ToString("D"), entity.PartitionKey);
        Assert.Equal("vault", entity.RowKey);
        Assert.Equal(keyRing.OwnerId, back.OwnerId);
        Assert.Equal(keyRing.MetadataKey.KeyId, back.MetadataKey.KeyId);
        Assert.Equal(keyRing.MetadataKey.Algorithm, back.MetadataKey.Algorithm);
        Assert.Equal(keyRing.MetadataKey.Value, back.MetadataKey.Value);
        Assert.Equal(keyRing.MetadataKeyVersion, back.MetadataKeyVersion);
        Assert.Equal(keyRing.CreatedAt, back.CreatedAt);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SecretTableEntity_Properties_ContainNoPlaintextColumns()
    {
        Assert.Equal(SecretColumns, PropertyNames(typeof(SecretTableEntity)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void FolderTableEntity_Properties_ContainNoPlaintextColumns()
    {
        Assert.Equal(FolderColumns, PropertyNames(typeof(FolderTableEntity)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SecretVersionTableEntity_Properties_ContainNoPlaintextColumns()
    {
        Assert.Equal(VersionColumns, PropertyNames(typeof(SecretVersionTableEntity)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void VaultKeyRingTableEntity_Properties_ContainNoPlaintextColumns()
    {
        Assert.Equal(KeyRingColumns, PropertyNames(typeof(VaultKeyRingTableEntity)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void BinaryColumns_AreEnvelopesOrWrappedKeysOnly()
    {
        var binary = new[] { typeof(SecretTableEntity), typeof(FolderTableEntity), typeof(SecretVersionTableEntity), typeof(VaultKeyRingTableEntity) }
            .SelectMany(type => type.GetProperties())
            .Where(property => property.PropertyType == typeof(byte[]))
            .Select(property => property.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["NameEnvelope", "NotesEnvelope", "TagsEnvelope", "UrlEnvelope", "UsernameEnvelope", "ValueEnvelope", "WrappedDataKey"], binary);
    }

    private static IEnumerable<string> PropertyNames(Type type) =>
        type.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);
}
