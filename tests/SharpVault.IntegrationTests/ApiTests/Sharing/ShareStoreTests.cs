// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
public sealed class ShareStoreTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly DateTimeOffset Epoch = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] IgnoredColumns = ["PartitionKey", "RowKey", "Timestamp", "odata.etag"];
    private static readonly string[] ExpectedColumns =
    [
        "GrantedAt", "GrantedByDisplayName", "GrantedById", "ItemId", "ItemKind", "ModifiedAt", "Permission",
        "RecipientDisplayName", "RecipientEmail", "RecipientId",
    ];

    private readonly Guid _owner = Guid.NewGuid();

    private IVaultStore Store
    {
        get
        {
            _ = Factory.Server;
            return Factory.Services.GetRequiredService<IVaultStore>();
        }
    }

    private TableClient VaultsTable => GetTableClient(TableNames.Vaults);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Guid OrderedId(int sequence) => new($"00000000-0000-0000-0000-{sequence:D12}");

    private ShareGrant NewGrant(
        Guid? itemId = null,
        ShareItemKind kind = ShareItemKind.Secret,
        Guid? recipient = null,
        SharePermission permission = SharePermission.View,
        Guid? owner = null) => new()
    {
        OwnerId = owner ?? _owner,
        ItemKind = kind,
        ItemId = itemId ?? Guid.NewGuid(),
        RecipientId = recipient ?? Guid.NewGuid(),
        Permission = permission,
        RecipientDisplayName = "Recipient " + Guid.NewGuid().ToString("N")[..6],
        RecipientEmail = "recipient@example.test",
        GrantedById = owner ?? _owner,
        GrantedByDisplayName = "Granting Owner",
        GrantedAt = Epoch,
        ModifiedAt = Epoch,
    };

    private Secret NewSecret(int sequence, Guid? folderId = null) => new()
    {
        Id = Guid.CreateVersion7(Epoch.AddMilliseconds(sequence * 3)),
        OwnerId = _owner,
        Type = SecretType.Login,
        FolderId = folderId,
        NameEnvelope = RandomNumberGenerator.GetBytes(40),
        UsernameEnvelope = RandomNumberGenerator.GetBytes(30),
        ValueEnvelope = RandomNumberGenerator.GetBytes(50),
        DataKey = new WrappedDataKey("key-1", "RSA-OAEP-256", RandomNumberGenerator.GetBytes(48)),
        MetadataKeyVersion = 1,
        Revision = 1,
        CreatedAt = Epoch,
        ModifiedAt = Epoch,
    };

    private Folder NewFolder(int sequence) => new()
    {
        Id = Guid.CreateVersion7(Epoch.AddMilliseconds(sequence * 3)),
        OwnerId = _owner,
        NameEnvelope = RandomNumberGenerator.GetBytes(40),
        MetadataKeyVersion = 1,
        CreatedAt = Epoch,
        ModifiedAt = Epoch,
    };

    private async Task<VaultWriteResult> AddAsync(params ShareGrant[] grants)
    {
        var result = await Store.SubmitAsync(_owner, [.. grants.Select(VaultChange.AddShareGrant)], Token);
        Assert.Equal(StoreWriteResult.Written, result.Result);
        return result;
    }

    private async Task<ShareGrantRows> RowsOf(ShareGrant grant) =>
        await Store.GetShareGrantRowsAsync(_owner, grant.ItemKind, grant.ItemId, grant.RecipientId, Token)
        ?? throw new InvalidOperationException("The grant rows are missing.");

    private async Task<(TableEntity ByItem, TableEntity ByRecipient)> RawRowsAsync(ShareGrant grant, Guid? owner = null)
    {
        var byItem = await VaultRows.GetAsync(VaultsTable, owner ?? _owner, VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId), Token);
        var byRecipient = await VaultRows.GetAsync(VaultsTable, owner ?? _owner, VaultKeys.GrantByRecipientRowKey(grant.RecipientId, grant.ItemKind, grant.ItemId), Token);
        return (byItem ?? throw new InvalidOperationException("No by-item row."), byRecipient ?? throw new InvalidOperationException("No by-recipient row."));
    }

    private static Dictionary<string, object> Columns(TableEntity row) =>
        row.Where(pair => !IgnoredColumns.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);

    private async Task<List<ShareGrant>> ListAllByOwnerAsync(int pageSize, List<int>? pageSizesSeen = null)
    {
        var all = new List<ShareGrant>();
        string? after = null;
        for (var guard = 0; guard < 200; guard++)
        {
            var page = await Store.ListShareGrantsByOwnerAsync(_owner, pageSize, after, Token);
            pageSizesSeen?.Add(page.Items.Count);
            all.AddRange(page.Items.Select(item => item.Value));
            if (page.ContinuationToken is null)
            {
                return all;
            }

            Assert.NotEqual(after, page.ContinuationToken);
            after = page.ContinuationToken;
        }

        throw new InvalidOperationException("The listing never ended.");
    }

    [Fact]
    public async Task AddShareGrant_WritesBothRowsWithIdenticalColumns()
    {
        var grant = NewGrant(kind: ShareItemKind.Folder, permission: SharePermission.Manage);

        var result = await AddAsync(grant);

        var (byItem, byRecipient) = await RawRowsAsync(grant);
        Assert.Equal(Columns(byItem), Columns(byRecipient));
        Assert.Equal(ExpectedColumns.Order(), Columns(byItem).Keys.Order());
        Assert.Equal("Folder", byItem.GetString("ItemKind"));
        Assert.Equal("Manage", byItem.GetString("Permission"));
        Assert.Equal(grant.RecipientId.ToString("D"), byItem.GetString("RecipientId"));
        Assert.Equal(grant.ItemId.ToString("D"), byItem.GetString("ItemId"));
        Assert.Equal(_owner.ToString("D"), byItem.PartitionKey);
        Assert.Equal(_owner.ToString("D"), byRecipient.PartitionKey);
        Assert.Equal(byItem.ETag.ToString(), result.ETags[0]);
    }

    [Fact]
    public async Task AddShareGrant_ThenGet_ReturnsEveryField()
    {
        var grant = NewGrant(permission: SharePermission.Edit);
        await AddAsync(grant);

        var stored = await Store.GetShareGrantAsync(_owner, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.NotNull(stored);
        Assert.Equal(grant.OwnerId, stored.Value.OwnerId);
        Assert.Equal(grant.ItemKind, stored.Value.ItemKind);
        Assert.Equal(grant.ItemId, stored.Value.ItemId);
        Assert.Equal(grant.RecipientId, stored.Value.RecipientId);
        Assert.Equal(SharePermission.Edit, stored.Value.Permission);
        Assert.Equal(grant.RecipientDisplayName, stored.Value.RecipientDisplayName);
        Assert.Equal(grant.RecipientEmail, stored.Value.RecipientEmail);
        Assert.Equal(grant.GrantedById, stored.Value.GrantedById);
        Assert.Equal(grant.GrantedByDisplayName, stored.Value.GrantedByDisplayName);
        Assert.Equal(grant.GrantedAt, stored.Value.GrantedAt);
        Assert.Equal(grant.ModifiedAt, stored.Value.ModifiedAt);
        Assert.False(string.IsNullOrWhiteSpace(stored.ETag));
    }

    [Fact]
    public async Task AddShareGrant_NullEmail_RoundTripsAsNull()
    {
        var grant = NewGrant();
        grant.RecipientEmail = null;
        await AddAsync(grant);

        var stored = await Store.GetShareGrantAsync(_owner, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Null(stored!.Value.RecipientEmail);
    }

    [Fact]
    public async Task AddShareGrant_SameGrantTwice_ReturnsAlreadyExistsAndKeepsTheFirst()
    {
        var grant = NewGrant(permission: SharePermission.View);
        await AddAsync(grant);
        var before = await RowsOf(grant);
        var second = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Manage);

        var result = await Store.SubmitAsync(_owner, [VaultChange.AddShareGrant(second)], Token);

        Assert.Equal(StoreWriteResult.AlreadyExists, result.Result);
        var after = await RowsOf(grant);
        Assert.Equal(before.ByItem.ETag, after.ByItem.ETag);
        Assert.Equal(before.ByRecipientETag, after.ByRecipientETag);
        Assert.Equal(SharePermission.View, after.ByItem.Value.Permission);
    }

    [Fact]
    public async Task AddShareGrant_TwoRacingAdds_OneWinsAndTheOtherIsAlreadyExists()
    {
        var recipient = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var first = NewGrant(itemId, recipient: recipient, permission: SharePermission.View);
        var second = NewGrant(itemId, recipient: recipient, permission: SharePermission.Edit);

        var results = await Task.WhenAll(
            Store.SubmitAsync(_owner, [VaultChange.AddShareGrant(first)], Token),
            Store.SubmitAsync(_owner, [VaultChange.AddShareGrant(second)], Token));

        Assert.Single(results, result => result.Result == StoreWriteResult.Written);
        Assert.Single(results, result => result.Result == StoreWriteResult.AlreadyExists);
        var (byItem, byRecipient) = await RawRowsAsync(first);
        Assert.Equal(Columns(byItem), Columns(byRecipient));
    }

    [Fact]
    public async Task AddShareGrant_WithAnExistingSecretAddInTheSameWrite_WritesNeitherGrantRow()
    {
        var existing = NewSecret(0);
        await Store.SubmitAsync(_owner, [VaultChange.AddSecret(existing)], Token);
        var grant = NewGrant();

        var result = await Store.SubmitAsync(_owner, [VaultChange.AddShareGrant(grant), VaultChange.AddSecret(existing)], Token);

        Assert.Equal(StoreWriteResult.AlreadyExists, result.Result);
        Assert.Null(await Store.GetShareGrantAsync(_owner, grant.ItemKind, grant.ItemId, grant.RecipientId, Token));
        Assert.Null(await VaultRows.GetAsync(VaultsTable, _owner, VaultKeys.GrantByRecipientRowKey(grant.RecipientId, grant.ItemKind, grant.ItemId), Token));
    }

    [Fact]
    public async Task ReplaceShareGrant_CurrentETags_ChangesBothRowsAndBothETags()
    {
        var grant = NewGrant(permission: SharePermission.View);
        await AddAsync(grant);
        var before = await RowsOf(grant);
        var changed = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Manage);
        changed.ModifiedAt = Epoch.AddHours(1);

        var result = await Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(changed, before.ByItem.ETag, before.ByRecipientETag)], Token);

        Assert.Equal(StoreWriteResult.Written, result.Result);
        var after = await RowsOf(grant);
        Assert.Equal(SharePermission.Manage, after.ByItem.Value.Permission);
        Assert.Equal(result.ETags[0], after.ByItem.ETag);
        Assert.NotEqual(before.ByItem.ETag, after.ByItem.ETag);
        Assert.NotEqual(before.ByRecipientETag, after.ByRecipientETag);
        var (byItem, byRecipient) = await RawRowsAsync(grant);
        Assert.Equal(Columns(byItem), Columns(byRecipient));
        Assert.Equal("Manage", byRecipient.GetString("Permission"));
    }

    [Fact]
    public async Task ReplaceShareGrant_StaleByRecipientETag_ReturnsConflict()
    {
        var grant = NewGrant(permission: SharePermission.View);
        await AddAsync(grant);
        var before = await RowsOf(grant);
        var changed = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Manage);

        var result = await Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(changed, before.ByItem.ETag, "W/\"datetime'2020-01-01T00%3A00%3A00.0000000Z'\"")], Token);

        Assert.Equal(StoreWriteResult.Conflict, result.Result);
        var after = await RowsOf(grant);
        Assert.Equal(before.ByItem.ETag, after.ByItem.ETag);
        Assert.Equal(before.ByRecipientETag, after.ByRecipientETag);
        Assert.Equal(SharePermission.View, after.ByItem.Value.Permission);
        var (byItem, byRecipient) = await RawRowsAsync(grant);
        Assert.Equal("View", byItem.GetString("Permission"));
        Assert.Equal("View", byRecipient.GetString("Permission"));
    }

    [Fact]
    public async Task ReplaceShareGrant_StaleByItemETag_ReturnsConflictAndChangesNeitherRow()
    {
        var grant = NewGrant(permission: SharePermission.View);
        await AddAsync(grant);
        var before = await RowsOf(grant);
        var changed = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Manage);

        var result = await Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(changed, "W/\"datetime'2020-01-01T00%3A00%3A00.0000000Z'\"", before.ByRecipientETag)], Token);

        Assert.Equal(StoreWriteResult.Conflict, result.Result);
        var (byItem, byRecipient) = await RawRowsAsync(grant);
        Assert.Equal("View", byItem.GetString("Permission"));
        Assert.Equal("View", byRecipient.GetString("Permission"));
    }

    [Fact]
    public async Task ReplaceShareGrant_ReusingTheOldETagsAfterAChange_ReturnsConflict()
    {
        var grant = NewGrant(permission: SharePermission.View);
        await AddAsync(grant);
        var before = await RowsOf(grant);
        var edit = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Edit);
        await Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(edit, before.ByItem.ETag, before.ByRecipientETag)], Token);
        var manage = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Manage);

        var result = await Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(manage, before.ByItem.ETag, before.ByRecipientETag)], Token);

        Assert.Equal(StoreWriteResult.Conflict, result.Result);
        Assert.Equal(SharePermission.Edit, (await RowsOf(grant)).ByItem.Value.Permission);
    }

    [Fact]
    public async Task ReplaceShareGrant_TwoRacingChangesFromTheSameETags_OneWinsAndRowsStayIdentical()
    {
        var grant = NewGrant(permission: SharePermission.View);
        await AddAsync(grant);
        var before = await RowsOf(grant);
        var toEdit = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Edit);
        var toManage = NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Manage);

        var results = await Task.WhenAll(
            Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(toEdit, before.ByItem.ETag, before.ByRecipientETag)], Token),
            Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(toManage, before.ByItem.ETag, before.ByRecipientETag)], Token));

        Assert.Single(results, result => result.Result == StoreWriteResult.Written);
        Assert.Single(results, result => result.Result == StoreWriteResult.Conflict);
        var (byItem, byRecipient) = await RawRowsAsync(grant);
        Assert.Equal(Columns(byItem), Columns(byRecipient));
    }

    [Fact]
    public async Task ReplaceShareGrant_Missing_ReturnsNotFound()
    {
        var grant = NewGrant();

        var result = await Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(grant, "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"", "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"")], Token);

        Assert.Equal(StoreWriteResult.NotFound, result.Result);
    }

    [Fact]
    public async Task DeleteShareGrant_RemovesBothRows()
    {
        var grant = NewGrant();
        await AddAsync(grant);
        var rows = await RowsOf(grant);

        var result = await Store.SubmitAsync(_owner, [VaultChange.DeleteShareGrant(grant, rows.ByItem.ETag, rows.ByRecipientETag)], Token);

        Assert.Equal(StoreWriteResult.Written, result.Result);
        Assert.Null(result.ETags[0]);
        Assert.Null(await VaultRows.GetAsync(VaultsTable, _owner, VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId), Token));
        Assert.Null(await VaultRows.GetAsync(VaultsTable, _owner, VaultKeys.GrantByRecipientRowKey(grant.RecipientId, grant.ItemKind, grant.ItemId), Token));
    }

    [Fact]
    public async Task DeleteShareGrant_StaleETag_ReturnsConflictAndKeepsBothRows()
    {
        var grant = NewGrant();
        await AddAsync(grant);
        var rows = await RowsOf(grant);
        var stale = "W/\"datetime'2020-01-01T00%3A00%3A00.0000000Z'\"";

        var byRecipientStale = await Store.SubmitAsync(_owner, [VaultChange.DeleteShareGrant(grant, rows.ByItem.ETag, stale)], Token);
        var byItemStale = await Store.SubmitAsync(_owner, [VaultChange.DeleteShareGrant(grant, stale, rows.ByRecipientETag)], Token);

        Assert.Equal(StoreWriteResult.Conflict, byRecipientStale.Result);
        Assert.Equal(StoreWriteResult.Conflict, byItemStale.Result);
        _ = await RawRowsAsync(grant);
    }

    [Fact]
    public async Task DeleteShareGrant_Missing_ReturnsNotFound()
    {
        var grant = NewGrant();
        const string etag = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";

        var result = await Store.SubmitAsync(_owner, [VaultChange.DeleteShareGrant(grant, etag, etag)], Token);

        Assert.Equal(StoreWriteResult.NotFound, result.Result);
    }

    [Fact]
    public async Task DeleteShareGrant_ThenAddAgain_Succeeds()
    {
        var grant = NewGrant();
        await AddAsync(grant);
        var rows = await RowsOf(grant);
        await Store.SubmitAsync(_owner, [VaultChange.DeleteShareGrant(grant, rows.ByItem.ETag, rows.ByRecipientETag)], Token);

        var again = await Store.SubmitAsync(_owner, [VaultChange.AddShareGrant(grant)], Token);

        Assert.Equal(StoreWriteResult.Written, again.Result);
    }

    [Fact]
    public async Task GetShareGrantRowsAsync_BothRows_ReturnsTheByItemGrantAndTheByRecipientETag()
    {
        var grant = NewGrant();
        await AddAsync(grant);

        var rows = await Store.GetShareGrantRowsAsync(_owner, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.NotNull(rows);
        var (byItem, byRecipient) = await RawRowsAsync(grant);
        Assert.Equal(byItem.ETag.ToString(), rows.ByItem.ETag);
        Assert.Equal(byRecipient.ETag.ToString(), rows.ByRecipientETag);
        Assert.Equal(grant.RecipientId, rows.ByItem.Value.RecipientId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetShareGrantRowsAsync_OneRowMissing_ReturnsNull(bool deleteByItem)
    {
        var grant = NewGrant();
        await AddAsync(grant);
        var rowKey = deleteByItem
            ? VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId)
            : VaultKeys.GrantByRecipientRowKey(grant.RecipientId, grant.ItemKind, grant.ItemId);
        await VaultsTable.DeleteEntityAsync(VaultKeys.PartitionKey(_owner), rowKey, cancellationToken: Token);

        var rows = await Store.GetShareGrantRowsAsync(_owner, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Null(rows);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GetShareGrantAsync_OtherOwner_ReturnsNull()
    {
        var grant = NewGrant();
        await AddAsync(grant);

        Assert.Null(await Store.GetShareGrantAsync(Guid.NewGuid(), grant.ItemKind, grant.ItemId, grant.RecipientId, Token));
        Assert.Null(await Store.GetShareGrantRowsAsync(Guid.NewGuid(), grant.ItemKind, grant.ItemId, grant.RecipientId, Token));
    }

    [Fact]
    public async Task GetShareGrantAsync_SameIdsOtherKind_ReturnsNull()
    {
        var grant = NewGrant(kind: ShareItemKind.Secret);
        await AddAsync(grant);

        Assert.Null(await Store.GetShareGrantAsync(_owner, ShareItemKind.Folder, grant.ItemId, grant.RecipientId, Token));
    }

    [Fact]
    public async Task ListShareGrantsForItemAsync_OtherItemsAndKinds_NotReturned()
    {
        var itemId = Guid.NewGuid();
        var wanted = new[] { NewGrant(itemId), NewGrant(itemId), NewGrant(itemId) };
        await AddAsync([.. wanted]);
        await AddAsync(NewGrant(), NewGrant(itemId, ShareItemKind.Folder), NewGrant(Guid.NewGuid(), ShareItemKind.Folder));

        var grants = await Store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, itemId, 100, Token);

        Assert.Equal(wanted.Select(grant => grant.RecipientId).Order(), grants.Select(grant => grant.Value.RecipientId).Order());
        Assert.All(grants, grant => Assert.Equal(itemId, grant.Value.ItemId));
        Assert.All(grants, grant => Assert.Equal(ShareItemKind.Secret, grant.Value.ItemKind));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListShareGrantsForItemAsync_SameItemIdUnderAnotherOwner_NotReturned()
    {
        var itemId = Guid.NewGuid();
        var other = Guid.NewGuid();
        await AddAsync(NewGrant(itemId));
        await Store.SubmitAsync(other, [VaultChange.AddShareGrant(NewGrant(itemId, owner: other))], Token);

        var grants = await Store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, itemId, 100, Token);

        Assert.Single(grants);
        Assert.Equal(_owner, grants[0].Value.OwnerId);
    }

    [Fact]
    public async Task ListShareGrantsForItemAsync_MoreThanTheLimit_ReturnsExactlyTheLimit()
    {
        var itemId = Guid.NewGuid();
        await AddAsync([.. Enumerable.Range(0, 12).Select(_ => NewGrant(itemId))]);

        var grants = await Store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, itemId, 5, Token);

        Assert.Equal(5, grants.Count);
    }

    [Fact]
    public async Task ListShareGrantsForItemAsync_NoGrants_ReturnsEmpty()
    {
        Assert.Empty(await Store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Folder, Guid.NewGuid(), 10, Token));
    }

    [Fact]
    public async Task ListFolderGrantsToRecipientAsync_SecretGrantsAndOtherRecipients_NotReturned()
    {
        var recipient = Guid.NewGuid();
        var folders = new[] { NewGrant(kind: ShareItemKind.Folder, recipient: recipient), NewGrant(kind: ShareItemKind.Folder, recipient: recipient) };
        await AddAsync([.. folders]);
        await AddAsync(
            NewGrant(kind: ShareItemKind.Secret, recipient: recipient),
            NewGrant(kind: ShareItemKind.Folder),
            NewGrant(kind: ShareItemKind.Secret));

        var grants = await Store.ListFolderGrantsToRecipientAsync(_owner, recipient, Token);

        Assert.Equal(folders.Select(grant => grant.ItemId).Order(), grants.Select(grant => grant.ItemId).Order());
        Assert.All(grants, grant => Assert.Equal(ShareItemKind.Folder, grant.ItemKind));
        Assert.All(grants, grant => Assert.Equal(recipient, grant.RecipientId));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListFolderGrantsToRecipientAsync_SameRecipientUnderAnotherOwner_NotReturned()
    {
        var recipient = Guid.NewGuid();
        var other = Guid.NewGuid();
        await Store.SubmitAsync(other, [VaultChange.AddShareGrant(NewGrant(kind: ShareItemKind.Folder, recipient: recipient, owner: other))], Token);

        Assert.Empty(await Store.ListFolderGrantsToRecipientAsync(_owner, recipient, Token));
    }

    [Fact]
    public async Task ListGrantsToRecipientBetweenAsync_ReturnsOnlyTheRange()
    {
        var recipient = Guid.NewGuid();
        var secrets = Enumerable.Range(1, 5).Select(index => NewGrant(OrderedId(index), recipient: recipient)).ToList();
        var folders = Enumerable.Range(1, 3).Select(index => NewGrant(OrderedId(index), ShareItemKind.Folder, recipient)).ToList();
        await AddAsync([.. secrets, .. folders, NewGrant(OrderedId(3)), NewGrant(OrderedId(3), ShareItemKind.Folder)]);

        var secretsTwoToFour = await Store.ListGrantsToRecipientBetweenAsync(
            _owner, recipient, new ShareItemRef(ShareItemKind.Secret, OrderedId(2)), new ShareItemRef(ShareItemKind.Secret, OrderedId(4)), Token);
        var folderTwoToSecretOne = await Store.ListGrantsToRecipientBetweenAsync(
            _owner, recipient, new ShareItemRef(ShareItemKind.Folder, OrderedId(2)), new ShareItemRef(ShareItemKind.Secret, OrderedId(1)), Token);
        var single = await Store.ListGrantsToRecipientBetweenAsync(
            _owner, recipient, new ShareItemRef(ShareItemKind.Secret, OrderedId(5)), new ShareItemRef(ShareItemKind.Secret, OrderedId(5)), Token);
        var reversed = await Store.ListGrantsToRecipientBetweenAsync(
            _owner, recipient, new ShareItemRef(ShareItemKind.Secret, OrderedId(4)), new ShareItemRef(ShareItemKind.Secret, OrderedId(2)), Token);

        Assert.Equal([OrderedId(2), OrderedId(3), OrderedId(4)], secretsTwoToFour.Select(grant => grant.ItemId));
        Assert.All(secretsTwoToFour, grant => Assert.Equal(ShareItemKind.Secret, grant.ItemKind));
        Assert.Equal(
            [(ShareItemKind.Folder, OrderedId(2)), (ShareItemKind.Folder, OrderedId(3)), (ShareItemKind.Secret, OrderedId(1))],
            folderTwoToSecretOne.Select(grant => (grant.ItemKind, grant.ItemId)));
        Assert.Equal(OrderedId(5), Assert.Single(single).ItemId);
        Assert.Empty(reversed);
        Assert.All(secretsTwoToFour.Concat(folderTwoToSecretOne).Concat(single), grant => Assert.Equal(recipient, grant.RecipientId));
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_PagesAcrossAllGrants()
    {
        var grants = Enumerable.Range(1, 7).Select(index => NewGrant(OrderedId(index), index % 2 == 0 ? ShareItemKind.Folder : ShareItemKind.Secret)).ToList();
        await AddAsync([.. grants]);
        var otherOwner = Guid.NewGuid();
        await Store.SubmitAsync(otherOwner, [VaultChange.AddShareGrant(NewGrant(owner: otherOwner))], Token);

        var listed = await ListAllByOwnerAsync(3);

        Assert.Equal(7, listed.Count);
        Assert.Equal(grants.Select(grant => grant.ItemId).Order(), listed.Select(grant => grant.ItemId).Order());
        Assert.All(listed, grant => Assert.Equal(_owner, grant.OwnerId));
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_ExactPageBoundary_EndsWithoutLoopingOrDuplicating()
    {
        await AddAsync([.. Enumerable.Range(1, 6).Select(index => NewGrant(OrderedId(index)))]);

        var listed = await ListAllByOwnerAsync(3);

        Assert.Equal(6, listed.Count);
        Assert.Equal(6, listed.Select(grant => (grant.ItemId, grant.RecipientId)).Distinct().Count());
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_Empty_ReturnsEmptyWithoutToken()
    {
        var page = await Store.ListShareGrantsByOwnerAsync(_owner, 10, null, Token);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_DamagedRowsBetweenGoodOnes_ListingContinuesPastEmptyPagesAndEnds()
    {
        var good = new[] { NewGrant(OrderedId(1)), NewGrant(OrderedId(4)) };
        await AddAsync([.. good]);
        await WriteDamagedGrantRowAsync(OrderedId(2));
        await WriteDamagedGrantRowAsync(OrderedId(3));
        var sizes = new List<int>();

        var listed = await ListAllByOwnerAsync(1, sizes);

        Assert.Equal(good.Select(grant => grant.ItemId), listed.Select(grant => grant.ItemId));
        Assert.Contains(0, sizes);
        Assert.True(sizes.Count <= 6);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_DamagedRowFirstAndLast_StillReturnsTheGoodRowsAndEnds()
    {
        var good = NewGrant(OrderedId(5));
        await AddAsync(good);
        await WriteDamagedGrantRowAsync(OrderedId(1));
        await WriteDamagedGrantRowAsync(OrderedId(9));

        var listed = await ListAllByOwnerAsync(1);

        Assert.Equal(good.ItemId, Assert.Single(listed).ItemId);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_OnlyDamagedRows_ReturnsEmptyAndEnds()
    {
        await WriteDamagedGrantRowAsync(OrderedId(1));
        await WriteDamagedGrantRowAsync(OrderedId(2));

        var sizes = new List<int>();
        var listed = await ListAllByOwnerAsync(1, sizes);

        Assert.Empty(listed);
        Assert.True(sizes.Count <= 3);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_DamagedRowAmongGoodOnes_LogsOnlyACount()
    {
        await AddAsync(NewGrant(OrderedId(1)));
        await WriteDamagedGrantRowAsync(OrderedId(2));
        Logs.Clear();

        await ListAllByOwnerAsync(10);

        var warning = Assert.Single(Logs.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Category == typeof(TableVaultStore).FullName);
        Assert.DoesNotContain(_owner.ToString("D"), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OrderedId(2).ToString("N"), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OrderedId(2).ToString("D"), warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    [Trait("Category", "Security")]
    public async Task ListShareGrantsByOwnerAsync_MalformedToken_ThrowsInvalidContinuationToken(string token)
    {
        await Assert.ThrowsAsync<SharpVault.Application.Common.Exceptions.InvalidContinuationTokenException>(() => Store.ListShareGrantsByOwnerAsync(_owner, 10, token, Token));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListShareGrantsByOwnerAsync_StorageTokenNamingAnotherPartition_StaysInTheOwnersPartition()
    {
        var other = Guid.NewGuid();
        var mine = NewGrant(OrderedId(1));
        await AddAsync(mine);
        await Store.SubmitAsync(other, [VaultChange.AddShareGrant(NewGrant(OrderedId(2), owner: other))], Token);
        var forged = ContinuationTokens.ForStorage($"{other:D}&gi_s_{OrderedId(1):N}");

        var outcome = await Record.ExceptionAsync(async () =>
        {
            var page = await Store.ListShareGrantsByOwnerAsync(_owner, 10, forged, Token);
            Assert.All(page.Items, item => Assert.Equal(_owner, item.Value.OwnerId));
        });

        Assert.True(outcome is null or SharpVault.Application.Common.Exceptions.InvalidContinuationTokenException);
    }

    [Fact]
    public async Task GrantRows_DoNotAppearInSecretFolderOrBinLists()
    {
        var folder = NewFolder(1);
        var secret = NewSecret(2, folder.Id);
        var rootSecret = NewSecret(3);
        var recycled = NewSecret(4);
        await Store.SubmitAsync(_owner, [VaultChange.AddFolder(folder), VaultChange.AddSecret(secret), VaultChange.AddSecret(rootSecret), VaultChange.AddSecret(recycled)], Token);
        var stored = (await Store.GetSecretAsync(_owner, recycled.Id, Token))!;
        recycled.DeletedAt = Epoch;
        recycled.DeletionBatchId = Guid.NewGuid();
        await Store.SubmitAsync(_owner, [VaultChange.RecycleSecret(recycled, stored.ETag)], Token);
        await AddAsync(
            NewGrant(secret.Id),
            NewGrant(folder.Id, ShareItemKind.Folder),
            NewGrant(recycled.Id),
            NewGrant(rootSecret.Id));

        var all = await Store.ListSecretsAsync(_owner, SecretFolderFilter.All, 100, null, Token);
        var unfiled = await Store.ListSecretsAsync(_owner, SecretFolderFilter.Unfiled, 100, null, Token);
        var inFolder = await Store.ListSecretsAsync(_owner, SecretFolderFilter.InFolder(folder.Id), 100, null, Token);
        var inFolderRows = await Store.ListSecretsInFolderAsync(_owner, folder.Id, 100, Token);
        var folders = await Store.ListFoldersAsync(_owner, Token);
        var bin = await Store.ListRecycleBinAsync(_owner, 100, null, Token);
        var batch = await Store.ListRecycledBeforeAsync(_owner, Epoch.AddDays(1), Token);
        var versions = await Store.ListVersionsAsync(_owner, secret.Id, Token);

        Assert.Equal(2, all.Items.Count);
        Assert.Single(unfiled.Items);
        Assert.Single(inFolder.Items);
        Assert.Single(inFolderRows);
        Assert.Single(folders);
        Assert.Single(bin.Items);
        Assert.Single(batch.Secrets);
        Assert.Empty(batch.Folders);
        Assert.Empty(versions);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GrantRows_ContainNoEnvelopeOrMarker()
    {
        var grant = NewGrant();
        await AddAsync(grant);

        var (byItem, byRecipient) = await RawRowsAsync(grant);

        foreach (var row in new[] { byItem, byRecipient })
        {
            Assert.Equal(ExpectedColumns.Order(), Columns(row).Keys.Order());
            Assert.DoesNotContain(row.Select(pair => pair.Value), value => value is byte[]);
            Assert.DoesNotContain(row.Keys, key => key is "BinaryLength" or "DeletedAt" or "DeletionBatchId" or "Revision" or "FolderKey");
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TamperedPermissionRow_ReadsAsNoAccess()
    {
        foreach (var tampered in new[] { "Owner", "4", "view", "", "Manage,Edit" })
        {
            var grant = NewGrant(permission: SharePermission.Manage);
            await AddAsync(grant);
            await VaultRows.SetPropertyAsync(VaultsTable, _owner, VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId), "Permission", tampered, Token);

            var stored = await Store.GetShareGrantAsync(_owner, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

            Assert.NotNull(stored);
            Assert.Equal(SharePermission.None, stored.Value.Permission);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GrantRow_CopiedIntoAnotherOwnersPartition_ReadsAsThatOwnersGrantNeverTheOriginal()
    {
        var grant = NewGrant(permission: SharePermission.Manage);
        await AddAsync(grant);
        var victim = Guid.NewGuid();
        var rowKey = VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId);
        await VaultRows.CopyRowToPartitionAsync(VaultsTable, _owner, rowKey, victim, Token);

        var read = await Store.GetShareGrantAsync(victim, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.NotNull(read);
        Assert.Equal(victim, read.Value.OwnerId);
        Assert.NotEqual(_owner, read.Value.OwnerId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ShareGrantChange_ForAnotherOwner_IsRejectedBeforeAnyRequest()
    {
        _ = Factory.Server;
        var foreign = NewGrant(owner: Guid.NewGuid());
        StorageRequests.Clear();

        await Assert.ThrowsAsync<ArgumentException>(() => Store.SubmitAsync(_owner, [VaultChange.AddShareGrant(foreign)], Token));

        Assert.Empty(StorageRequests.Requests);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ShareGrantReads_AllQueriesCarryThePartitionKeyAndCallCountsAreFixed()
    {
        var recipient = Guid.NewGuid();
        var grant = NewGrant(kind: ShareItemKind.Folder, recipient: recipient);
        await AddAsync(grant, NewGrant(grant.ItemId, ShareItemKind.Folder), NewGrant(kind: ShareItemKind.Folder, recipient: recipient));
        _ = Factory.Server;

        StorageRequests.Clear();
        await Store.GetShareGrantAsync(_owner, grant.ItemKind, grant.ItemId, recipient, Token);
        var afterGet = StorageRequests.Count;
        await Store.GetShareGrantRowsAsync(_owner, grant.ItemKind, grant.ItemId, recipient, Token);
        var afterRows = StorageRequests.Count;
        await Store.ListShareGrantsForItemAsync(_owner, grant.ItemKind, grant.ItemId, 100, Token);
        var afterItemList = StorageRequests.Count;
        await Store.ListFolderGrantsToRecipientAsync(_owner, recipient, Token);
        var afterFolderList = StorageRequests.Count;
        await Store.ListGrantsToRecipientBetweenAsync(_owner, recipient, new ShareItemRef(ShareItemKind.Folder, OrderedId(0)), new ShareItemRef(ShareItemKind.Secret, OrderedId(9)), Token);
        var afterBetween = StorageRequests.Count;
        await Store.ListShareGrantsByOwnerAsync(_owner, 50, null, Token);
        var afterOwnerList = StorageRequests.Count;

        Assert.Equal(1, afterGet);
        Assert.Equal(2, afterRows - afterGet);
        Assert.Equal(1, afterItemList - afterRows);
        Assert.Equal(1, afterFolderList - afterItemList);
        Assert.Equal(1, afterBetween - afterFolderList);
        Assert.Equal(1, afterOwnerList - afterBetween);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
        Assert.All(StorageRequests.Requests.Where(request => request.IsEntityQuery), request => Assert.Contains(_owner.ToString("D"), request.Filter, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ShareGrantWrites_AddReplaceDeleteEachUseOneTransactionRequest()
    {
        var grant = NewGrant();
        _ = Factory.Server;

        StorageRequests.Clear();
        await AddAsync(grant);
        var addRequests = StorageRequests.Count;
        var rows = await RowsOf(grant);
        StorageRequests.Clear();
        await Store.SubmitAsync(_owner, [VaultChange.ReplaceShareGrant(NewGrant(grant.ItemId, grant.ItemKind, grant.RecipientId, SharePermission.Edit), rows.ByItem.ETag, rows.ByRecipientETag)], Token);
        var replaceRequests = StorageRequests.Count;
        var changed = await RowsOf(grant);
        StorageRequests.Clear();
        await Store.SubmitAsync(_owner, [VaultChange.DeleteShareGrant(grant, changed.ByItem.ETag, changed.ByRecipientETag)], Token);

        Assert.Equal(1, addRequests);
        Assert.Equal(1, replaceRequests);
        Assert.Equal(1, StorageRequests.Count);
    }

    private async Task WriteDamagedGrantRowAsync(Guid itemId)
    {
        var grant = NewGrant(itemId);
        var row = new TableEntity(VaultKeys.PartitionKey(_owner), VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId))
        {
            ["ItemKind"] = "Bogus",
            ["ItemId"] = itemId.ToString("D"),
            ["RecipientId"] = grant.RecipientId.ToString("D"),
            ["Permission"] = "View",
            ["RecipientDisplayName"] = "x",
            ["GrantedById"] = _owner.ToString("D"),
            ["GrantedByDisplayName"] = "x",
            ["GrantedAt"] = Epoch,
            ["ModifiedAt"] = Epoch,
        };
        await VaultsTable.AddEntityAsync(row, Token);
    }
}
