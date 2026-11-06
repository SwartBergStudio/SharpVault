// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Sharing;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

public sealed class ShareGrantMappingTests
{
    private static readonly string[] GrantColumns =
    [
        "ETag", "GrantedAt", "GrantedByDisplayName", "GrantedById", "ItemId", "ItemKind", "ModifiedAt", "PartitionKey",
        "Permission", "RecipientDisplayName", "RecipientEmail", "RecipientId", "RowKey", "Timestamp",
    ];

    private static readonly string[] InboxColumns =
    [
        "ETag", "FiledFolderKey", "ItemId", "ItemKind", "OwnerId", "PartitionKey", "RowKey", "SharedAt", "Timestamp",
    ];

    [Theory]
    [InlineData(ShareItemKind.Secret, SharePermission.View)]
    [InlineData(ShareItemKind.Secret, SharePermission.Edit)]
    [InlineData(ShareItemKind.Folder, SharePermission.Manage)]
    public void Grant_RoundTripsEveryColumnOnBothRows(ShareItemKind kind, SharePermission permission)
    {
        var grant = ShareSamples.Grant(Guid.NewGuid(), kind, permission: permission);

        foreach (var byItem in new[] { true, false })
        {
            var back = VaultStorageMappingRegister.ToDomain(ShareSamples.GrantRow(grant, byItem));

            Assert.Equal(grant.OwnerId, back.OwnerId);
            Assert.Equal(grant.ItemKind, back.ItemKind);
            Assert.Equal(grant.ItemId, back.ItemId);
            Assert.Equal(grant.RecipientId, back.RecipientId);
            Assert.Equal(grant.Permission, back.Permission);
            Assert.Equal(grant.RecipientDisplayName, back.RecipientDisplayName);
            Assert.Equal(grant.RecipientEmail, back.RecipientEmail);
            Assert.Equal(grant.GrantedById, back.GrantedById);
            Assert.Equal(grant.GrantedByDisplayName, back.GrantedByDisplayName);
            Assert.Equal(grant.GrantedAt, back.GrantedAt);
            Assert.Equal(grant.ModifiedAt, back.ModifiedAt);
        }
    }

    [Fact]
    public void Grant_NullRecipientEmail_RoundTripsAsNull()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        grant.RecipientEmail = null;

        Assert.Null(VaultStorageMappingRegister.ToDomain(ShareSamples.GrantRow(grant)).RecipientEmail);
    }

    [Fact]
    public void Grant_BothRows_HaveIdenticalColumnsAndOnlyTheRowKeyDiffers()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid(), ShareItemKind.Folder);

        var byItem = ShareSamples.GrantRow(grant, byItem: true);
        var byRecipient = ShareSamples.GrantRow(grant, byItem: false);

        Assert.Equal(GrantColumns.Order(), typeof(ShareGrantTableEntity).GetProperties().Select(property => property.Name).Order());
        Assert.NotEqual(byItem.RowKey, byRecipient.RowKey);
        Assert.Equal(byItem.PartitionKey, byRecipient.PartitionKey);
        foreach (var property in typeof(ShareGrantTableEntity).GetProperties().Where(property => property.Name is not ("RowKey" or "ETag" or "Timestamp")))
        {
            Assert.Equal(property.GetValue(byItem), property.GetValue(byRecipient));
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Grant_Columns_HoldNoEnvelopeNameOrValue()
    {
        var names = typeof(ShareGrantTableEntity).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(names, name => name.Contains("Envelope", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("DataKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Value", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name is "Name" or "Notes" or "Username");
    }

    [Fact]
    public void Grant_EntityPermissionAndKind_AreStoredAsNames()
    {
        var entity = ShareSamples.GrantRow(ShareSamples.Grant(Guid.NewGuid(), ShareItemKind.Folder, permission: SharePermission.Manage));

        Assert.Equal("Folder", entity.ItemKind);
        Assert.Equal("Manage", entity.Permission);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("4")]
    [InlineData("2")]
    [InlineData("1")]
    [InlineData("view")]
    [InlineData("EDIT")]
    [InlineData("")]
    [InlineData(" View")]
    [InlineData("View ")]
    [InlineData("Manage,Edit")]
    [InlineData("None")]
    [Trait("Category", "Security")]
    public void ParsePermission_TamperedText_FailsClosedToNone(string text)
    {
        Assert.Equal(SharePermission.None, VaultStorageMappingRegister.ParsePermission(text));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ParsePermission_Null_FailsClosedToNone()
    {
        Assert.Equal(SharePermission.None, VaultStorageMappingRegister.ParsePermission(null));
    }

    [Theory]
    [InlineData("View", SharePermission.View)]
    [InlineData("Edit", SharePermission.Edit)]
    [InlineData("Manage", SharePermission.Manage)]
    public void ParsePermission_ExactNames_Map(string text, SharePermission expected)
    {
        Assert.Equal(expected, VaultStorageMappingRegister.ParsePermission(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("secret")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("Secret ")]
    [InlineData("Bogus")]
    public void ParseItemKind_UnknownOrNumericText_ThrowsInvalidData(string text)
    {
        Assert.Throws<InvalidDataException>(() => VaultStorageMappingRegister.ParseItemKind(text));
    }

    [Fact]
    public void Grant_TamperedPermissionRow_ReadsAsNone()
    {
        var row = ShareSamples.GrantRow(ShareSamples.Grant(Guid.NewGuid(), permission: SharePermission.Manage));
        row.Permission = "4";

        Assert.Equal(SharePermission.None, VaultStorageMappingRegister.ToDomain(row).Permission);
    }

    [Fact]
    public void Grant_UnknownItemKindRow_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => VaultStorageMappingRegister.ToDomain(ShareSamples.DamagedGrantRow(Guid.NewGuid())));
    }

    [Fact]
    public void Grant_NonGuidColumn_ThrowsFormat()
    {
        var row = ShareSamples.GrantRow(ShareSamples.Grant(Guid.NewGuid()));
        row.RecipientId = "not-a-guid";

        Assert.Throws<FormatException>(() => VaultStorageMappingRegister.ToDomain(row));
    }

    [Fact]
    public void InboxEntry_RoundTripsEveryColumn()
    {
        var folder = Guid.NewGuid();
        var entry = ShareSamples.InboxEntry(Guid.NewGuid(), kind: ShareItemKind.Folder, filedFolderId: folder);

        var entity = ShareInboxMappingRegister.ToEntity(entry);
        var back = ShareInboxMappingRegister.ToDomain(entity);

        Assert.Equal(entry.RecipientId.ToString("D"), entity.PartitionKey);
        Assert.Equal(ShareInboxKeys.RowKey(entry.OwnerId, entry.ItemKind, entry.ItemId), entity.RowKey);
        Assert.Equal(entry.RecipientId, back.RecipientId);
        Assert.Equal(entry.OwnerId, back.OwnerId);
        Assert.Equal(entry.ItemKind, back.ItemKind);
        Assert.Equal(entry.ItemId, back.ItemId);
        Assert.Equal(entry.SharedAt, back.SharedAt);
        Assert.Equal(folder, back.FiledFolderId);
    }

    [Fact]
    public void InboxEntry_Unfiled_StoresNoFolderKeyAndReadsBackNull()
    {
        var entity = ShareInboxMappingRegister.ToEntity(ShareSamples.InboxEntry(Guid.NewGuid()));

        Assert.Null(entity.FiledFolderKey);
        Assert.Null(ShareInboxMappingRegister.ToDomain(entity).FiledFolderId);
    }

    [Fact]
    public void InboxEntry_Columns_HoldNoItemNameOrSecretMaterial()
    {
        var names = typeof(ShareInboxTableEntity).GetProperties().Select(property => property.Name).Order().ToList();

        Assert.Equal(InboxColumns.Order(), names);
    }

    [Fact]
    public void InboxEntry_MapsThroughMapster()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(VaultKeys).Assembly);
        var entry = ShareSamples.InboxEntry(Guid.NewGuid(), filedFolderId: Guid.NewGuid());

        var back = entry.Adapt<ShareInboxTableEntity>(config).Adapt<ShareInboxEntry>(config);

        Assert.Equal(entry.ItemId, back.ItemId);
        Assert.Equal(entry.FiledFolderId, back.FiledFolderId);
    }

    [Fact]
    public void InboxEntry_UnknownItemKind_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => ShareInboxMappingRegister.ToDomain(ShareSamples.DamagedInboxRow(Guid.NewGuid())));
    }

    [Fact]
    public void InboxRowKey_SortsByOwnerThenKindThenItem()
    {
        var lowOwner = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var highOwner = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        var item = Guid.NewGuid();

        var keys = new[]
        {
            ShareInboxKeys.RowKey(highOwner, ShareItemKind.Folder, item),
            ShareInboxKeys.RowKey(lowOwner, ShareItemKind.Secret, item),
            ShareInboxKeys.RowKey(lowOwner, ShareItemKind.Folder, item),
        };

        Assert.Equal([keys[2], keys[1], keys[0]], keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GrantRowKeys_FolderKindSortsBeforeSecretKind()
    {
        var recipient = Guid.NewGuid();
        var item = Guid.NewGuid();

        Assert.True(string.CompareOrdinal(
            VaultKeys.GrantByRecipientRowKey(recipient, ShareItemKind.Folder, item),
            VaultKeys.GrantByRecipientRowKey(recipient, ShareItemKind.Secret, item)) < 0);
    }

    [Fact]
    public void GrantRowKeys_AreDisjointFromSecretFolderBinAndVersionKeys()
    {
        var id = Guid.NewGuid();
        var grantKeys = new[]
        {
            VaultKeys.GrantByItemRowKey(ShareItemKind.Secret, id, id),
            VaultKeys.GrantByRecipientRowKey(id, ShareItemKind.Folder, id),
        };
        var vaultKeys = new[]
        {
            VaultKeys.SecretRowKey(id, recycled: false),
            VaultKeys.SecretRowKey(id, recycled: true),
            VaultKeys.FolderRowKey(id, recycled: false),
            VaultKeys.FolderRowKey(id, recycled: true),
            VaultKeys.VersionRowKey(id, id),
            VaultKeys.KeyRingRowKey,
        };

        Assert.Empty(grantKeys.Intersect(vaultKeys));
        Assert.All(grantKeys, key => Assert.DoesNotMatch("^(s|f|bs|bf|h)_", key));
    }

    [Fact]
    public void GrantRanges_ByItemAndByRecipient_AreSeparatedByPrefix()
    {
        var recipient = Guid.NewGuid();
        var item = Guid.NewGuid();

        Assert.StartsWith(VaultKeys.GrantByItemPrefix, VaultKeys.GrantByItemRowKey(ShareItemKind.Secret, item, recipient), StringComparison.Ordinal);
        Assert.StartsWith(VaultKeys.GrantByRecipientPrefix, VaultKeys.GrantByRecipientRowKey(recipient, ShareItemKind.Secret, item), StringComparison.Ordinal);
        Assert.StartsWith(VaultKeys.GrantRecipientFolderRangeStart(recipient), VaultKeys.GrantByRecipientRowKey(recipient, ShareItemKind.Folder, item), StringComparison.Ordinal);
        Assert.False(VaultKeys.GrantByRecipientRowKey(recipient, ShareItemKind.Secret, item).StartsWith(VaultKeys.GrantRecipientFolderRangeStart(recipient), StringComparison.Ordinal));
    }

    [Fact]
    public void KindCode_UnknownKind_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VaultKeys.KindCode((ShareItemKind)99));
    }
}
