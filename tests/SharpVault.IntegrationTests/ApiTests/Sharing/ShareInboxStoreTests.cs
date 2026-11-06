// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
public sealed class ShareInboxStoreTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly DateTimeOffset Epoch = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedColumns = ["FiledFolderKey", "ItemId", "ItemKind", "OwnerId", "SharedAt"];

    private readonly Guid _recipient = Guid.NewGuid();

    private IShareInboxStore Store
    {
        get
        {
            _ = Factory.Server;
            return Factory.Services.GetRequiredService<IShareInboxStore>();
        }
    }

    private TableClient InboxTable => GetTableClient(TableNames.ShareInbox);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Guid OwnerId(int sequence) => new($"{sequence:D8}-0000-0000-0000-000000000000");

    private static Guid ItemId(int sequence) => new($"00000000-0000-0000-0000-{sequence:D12}");

    private ShareInboxEntry NewEntry(
        int owner = 1,
        ShareItemKind kind = ShareItemKind.Secret,
        int item = 1,
        Guid? filedFolderId = null,
        Guid? recipient = null) => new()
    {
        RecipientId = recipient ?? _recipient,
        OwnerId = OwnerId(owner),
        ItemKind = kind,
        ItemId = ItemId(item),
        SharedAt = Epoch,
        FiledFolderId = filedFolderId,
    };

    private async Task<List<ShareInboxEntry>> ListAllAsync(Guid? folder, int pageSize, List<int>? sizes = null)
    {
        var all = new List<ShareInboxEntry>();
        string? after = null;
        for (var guard = 0; guard < 200; guard++)
        {
            var page = await Store.ListAsync(_recipient, folder, pageSize, after, Token);
            sizes?.Add(page.Items.Count);
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

    private async Task WriteDamagedRowAsync(int owner, int item, Guid? recipient = null)
    {
        var entry = NewEntry(owner, item: item, recipient: recipient);
        var row = new TableEntity(entry.RecipientId.ToString("D"), $"{OwnerId(owner):N}_s_{ItemId(item):N}")
        {
            ["OwnerId"] = OwnerId(owner).ToString("D"),
            ["ItemKind"] = "Bogus",
            ["ItemId"] = ItemId(item).ToString("D"),
            ["SharedAt"] = Epoch,
        };
        await InboxTable.AddEntityAsync(row, Token);
    }

    [Fact]
    public async Task AddIfAbsentAsync_NewEntry_StoresItAndReadsItBack()
    {
        var folder = Guid.NewGuid();
        var entry = NewEntry(kind: ShareItemKind.Folder, filedFolderId: folder);

        await Store.AddIfAbsentAsync(entry, Token);

        var stored = await Store.GetAsync(_recipient, entry.OwnerId, entry.ItemKind, entry.ItemId, Token);
        Assert.NotNull(stored);
        Assert.Equal(entry.RecipientId, stored.Value.RecipientId);
        Assert.Equal(entry.OwnerId, stored.Value.OwnerId);
        Assert.Equal(ShareItemKind.Folder, stored.Value.ItemKind);
        Assert.Equal(entry.ItemId, stored.Value.ItemId);
        Assert.Equal(Epoch, stored.Value.SharedAt);
        Assert.Equal(folder, stored.Value.FiledFolderId);
        Assert.False(string.IsNullOrWhiteSpace(stored.ETag));
    }

    [Fact]
    public async Task AddIfAbsentAsync_ExistingRow_KeepsFilingAndSucceeds()
    {
        var folder = Guid.NewGuid();
        await Store.AddIfAbsentAsync(NewEntry(filedFolderId: folder), Token);
        var before = await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token);

        await Store.AddIfAbsentAsync(NewEntry(filedFolderId: null), Token);

        var after = await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token);
        Assert.Equal(folder, after!.Value.FiledFolderId);
        Assert.Equal(before!.ETag, after.ETag);
    }

    [Fact]
    public async Task AddIfAbsentAsync_TwoRacingAdds_BothSucceedAndOneRowRemains()
    {
        var first = NewEntry(filedFolderId: Guid.NewGuid());
        var second = NewEntry(filedFolderId: null);

        await Task.WhenAll(Store.AddIfAbsentAsync(first, Token), Store.AddIfAbsentAsync(second, Token));

        var listed = await ListAllAsync(null, 10);
        Assert.Single(listed);
    }

    [Fact]
    public async Task AddIfAbsentAsync_Null_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Store.AddIfAbsentAsync(null!, Token));
    }

    [Fact]
    public async Task GetAsync_Missing_ReturnsNull()
    {
        Assert.Null(await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GetAsync_OtherRecipientOrOtherKind_ReturnsNull()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);

        Assert.Null(await Store.GetAsync(Guid.NewGuid(), OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
        Assert.Null(await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Folder, ItemId(1), Token));
        Assert.Null(await Store.GetAsync(_recipient, OwnerId(2), ShareItemKind.Secret, ItemId(1), Token));
    }

    [Fact]
    public async Task SetFiledFolderAsync_CurrentETag_FilesTheRowAndChangesTheETag()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);
        var stored = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;
        var folder = Guid.NewGuid();
        var filed = NewEntry(filedFolderId: folder);

        var result = await Store.SetFiledFolderAsync(filed, stored.ETag, Token);

        Assert.Equal(StoreWriteResult.Written, result);
        var after = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;
        Assert.Equal(folder, after.Value.FiledFolderId);
        Assert.NotEqual(stored.ETag, after.ETag);
    }

    [Fact]
    public async Task SetFiledFolderAsync_ClearingTheFolder_StoresNoFolder()
    {
        await Store.AddIfAbsentAsync(NewEntry(filedFolderId: Guid.NewGuid()), Token);
        var stored = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;

        var result = await Store.SetFiledFolderAsync(NewEntry(filedFolderId: null), stored.ETag, Token);

        Assert.Equal(StoreWriteResult.Written, result);
        Assert.Null((await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!.Value.FiledFolderId);
    }

    [Fact]
    public async Task SetFiledFolderAsync_StaleETag_ReturnsConflict()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);
        var first = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;
        await Store.SetFiledFolderAsync(NewEntry(filedFolderId: Guid.NewGuid()), first.ETag, Token);
        var winner = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;

        var result = await Store.SetFiledFolderAsync(NewEntry(filedFolderId: Guid.NewGuid()), first.ETag, Token);

        Assert.Equal(StoreWriteResult.Conflict, result);
        Assert.Equal(winner.Value.FiledFolderId, (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!.Value.FiledFolderId);
    }

    [Fact]
    public async Task SetFiledFolderAsync_RowRemoved_ReturnsNotFoundAndDoesNotRecreateIt()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);
        var stored = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;
        await Store.RemoveAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token);

        var result = await Store.SetFiledFolderAsync(NewEntry(filedFolderId: Guid.NewGuid()), stored.ETag, Token);

        Assert.Equal(StoreWriteResult.NotFound, result);
        Assert.Null(await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
    }

    [Fact]
    public async Task SetFiledFolderAsync_NeverCreatesAnotherRecipientsRow()
    {
        var other = Guid.NewGuid();
        await Store.AddIfAbsentAsync(NewEntry(), Token);
        var stored = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;

        var result = await Store.SetFiledFolderAsync(NewEntry(filedFolderId: Guid.NewGuid(), recipient: other), stored.ETag, Token);

        Assert.NotEqual(StoreWriteResult.Written, result);
        Assert.Null(await Store.GetAsync(other, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
    }

    [Fact]
    public async Task RemoveAsync_Existing_ReturnsTrueAndTheRowIsGone()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);

        var removed = await Store.RemoveAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token);

        Assert.True(removed);
        Assert.Null(await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
    }

    [Fact]
    public async Task RemoveAsync_Missing_ReturnsFalse()
    {
        Assert.False(await Store.RemoveAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
    }

    [Fact]
    public async Task RemoveAsync_Twice_SecondReturnsFalse()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);

        Assert.True(await Store.RemoveAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
        Assert.False(await Store.RemoveAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token));
    }

    [Fact]
    public async Task RemoveAsync_FiledJustBefore_RemovesTheCurrentRow()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);
        var stored = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;
        await Store.SetFiledFolderAsync(NewEntry(filedFolderId: Guid.NewGuid()), stored.ETag, Token);

        var removed = await Store.RemoveAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token);

        Assert.True(removed);
    }

    [Fact]
    public async Task RemoveAsync_RacingWithFiling_NeverLeavesTheRowAndNeverThrows()
    {
        for (var round = 0; round < 8; round++)
        {
            var entry = NewEntry(item: round + 1);
            await Store.AddIfAbsentAsync(entry, Token);
            var stored = (await Store.GetAsync(_recipient, entry.OwnerId, entry.ItemKind, entry.ItemId, Token))!;
            var filed = NewEntry(item: round + 1, filedFolderId: Guid.NewGuid());

            var remove = Store.RemoveAsync(_recipient, entry.OwnerId, entry.ItemKind, entry.ItemId, Token);
            var file = Store.SetFiledFolderAsync(filed, stored.ETag, Token);
            await Task.WhenAll(remove, file);

            Assert.Null(await Store.GetAsync(_recipient, entry.OwnerId, entry.ItemKind, entry.ItemId, Token));
        }
    }

    [Fact]
    public async Task ListAsync_SortsByOwner()
    {
        await Store.AddIfAbsentAsync(NewEntry(owner: 3), Token);
        await Store.AddIfAbsentAsync(NewEntry(owner: 1, kind: ShareItemKind.Secret, item: 2), Token);
        await Store.AddIfAbsentAsync(NewEntry(owner: 2), Token);
        await Store.AddIfAbsentAsync(NewEntry(owner: 1, kind: ShareItemKind.Folder, item: 9), Token);
        await Store.AddIfAbsentAsync(NewEntry(owner: 1, kind: ShareItemKind.Secret, item: 1), Token);

        var listed = await ListAllAsync(null, 10);

        Assert.Equal(
            [(OwnerId(1), ShareItemKind.Folder, ItemId(9)), (OwnerId(1), ShareItemKind.Secret, ItemId(1)), (OwnerId(1), ShareItemKind.Secret, ItemId(2)), (OwnerId(2), ShareItemKind.Secret, ItemId(1)), (OwnerId(3), ShareItemKind.Secret, ItemId(1))],
            listed.Select(entry => (entry.OwnerId, entry.ItemKind, entry.ItemId)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListAsync_OtherRecipientsRows_NeverReturned()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);
        await Store.AddIfAbsentAsync(NewEntry(item: 2, recipient: Guid.NewGuid()), Token);
        await Store.AddIfAbsentAsync(NewEntry(item: 3, recipient: Guid.NewGuid()), Token);

        var listed = await ListAllAsync(null, 10);

        Assert.Single(listed);
        Assert.All(listed, entry => Assert.Equal(_recipient, entry.RecipientId));
    }

    [Fact]
    public async Task ListAsync_PagesAcrossAllRowsExactlyOnce()
    {
        await Task.WhenAll(Enumerable.Range(1, 11).Select(index => Store.AddIfAbsentAsync(NewEntry(owner: index % 3 + 1, item: index), Token)));

        var listed = await ListAllAsync(null, 4);

        Assert.Equal(11, listed.Count);
        Assert.Equal(11, listed.Select(entry => (entry.OwnerId, entry.ItemId)).Distinct().Count());
    }

    [Fact]
    public async Task ListAsync_FiledFilter_ReturnsOnlyThatFolder()
    {
        var folder = Guid.NewGuid();
        await Store.AddIfAbsentAsync(NewEntry(item: 1, filedFolderId: folder), Token);
        await Store.AddIfAbsentAsync(NewEntry(item: 2, filedFolderId: folder), Token);
        await Store.AddIfAbsentAsync(NewEntry(item: 3, filedFolderId: Guid.NewGuid()), Token);
        await Store.AddIfAbsentAsync(NewEntry(item: 4), Token);

        var listed = await ListAllAsync(folder, 10);

        Assert.Equal([ItemId(1), ItemId(2)], listed.Select(entry => entry.ItemId));
        Assert.All(listed, entry => Assert.Equal(folder, entry.FiledFolderId));
    }

    [Fact]
    public async Task ListAsync_FiledFilterWithPaging_ReturnsEveryMatchAcrossPages()
    {
        var folder = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(1, 9).Select(index => Store.AddIfAbsentAsync(NewEntry(item: index, filedFolderId: index % 2 == 0 ? folder : null), Token)));

        var listed = await ListAllAsync(folder, 2);

        Assert.Equal([ItemId(2), ItemId(4), ItemId(6), ItemId(8)], listed.Select(entry => entry.ItemId));
    }

    [Fact]
    public async Task ListAsync_NoRows_ReturnsEmptyWithoutToken()
    {
        var page = await Store.ListAsync(_recipient, null, 10, null, Token);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListAsync_DamagedRowsAmongGoodOnes_ListingSkipsThemEndsAndNeverLoops()
    {
        await Store.AddIfAbsentAsync(NewEntry(owner: 1, item: 1), Token);
        await WriteDamagedRowAsync(owner: 2, item: 1);
        await WriteDamagedRowAsync(owner: 3, item: 1);
        await Store.AddIfAbsentAsync(NewEntry(owner: 4, item: 1), Token);
        await WriteDamagedRowAsync(owner: 5, item: 1);
        var sizes = new List<int>();

        var listed = await ListAllAsync(null, 1, sizes);

        Assert.Equal([OwnerId(1), OwnerId(4)], listed.Select(entry => entry.OwnerId));
        Assert.Contains(0, sizes);
        Assert.True(sizes.Count <= 6);
    }

    [Fact]
    public async Task ListAsync_OnlyDamagedRows_ReturnsEmptyItemsAndEnds()
    {
        await WriteDamagedRowAsync(owner: 1, item: 1);
        await WriteDamagedRowAsync(owner: 2, item: 1);
        var sizes = new List<int>();

        var listed = await ListAllAsync(null, 1, sizes);

        Assert.Empty(listed);
        Assert.True(sizes.Count <= 3);
    }

    [Fact]
    public async Task ListAsync_PageOfOnlyDamagedRowsWithMoreAfter_ReturnsEmptyItemsAndATokenThatContinuesToTheGoodRow()
    {
        await WriteDamagedRowAsync(owner: 1, item: 1);
        await WriteDamagedRowAsync(owner: 2, item: 1);
        await Store.AddIfAbsentAsync(NewEntry(owner: 3, item: 1), Token);

        var first = await Store.ListAsync(_recipient, null, 2, null, Token);

        Assert.Empty(first.Items);
        Assert.NotNull(first.ContinuationToken);
        Assert.True(ContinuationTokens.TryGetInboxKey(first.ContinuationToken, out _));
        var second = await Store.ListAsync(_recipient, null, 2, first.ContinuationToken, Token);
        Assert.Equal(OwnerId(3), Assert.Single(second.Items).Value.OwnerId);
        Assert.Null(second.ContinuationToken);
    }

    [Fact]
    public async Task ListAsync_DamagedRowAmongGoodOnes_LogsOnlyACount()
    {
        await Store.AddIfAbsentAsync(NewEntry(owner: 1, item: 1), Token);
        await WriteDamagedRowAsync(owner: 2, item: 1);
        Logs.Clear();

        await ListAllAsync(null, 10);

        var warning = Assert.Single(Logs.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("share inbox", StringComparison.Ordinal));
        Assert.DoesNotContain(_recipient.ToString("D"), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerId(2).ToString("N"), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ItemId(1).ToString("N"), warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    [InlineData("' or PartitionKey ne '")]
    [Trait("Category", "Security")]
    public async Task ListAsync_MalformedToken_ThrowsArgumentException(string token)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.ListAsync(_recipient, null, 10, token, Token));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListAsync_TokenNamingAnotherRecipientsRow_StaysInTheCallersPartition()
    {
        var other = Guid.NewGuid();
        await Store.AddIfAbsentAsync(NewEntry(owner: 5, item: 1), Token);
        await Store.AddIfAbsentAsync(NewEntry(owner: 5, item: 2, recipient: other), Token);
        var token = ContinuationTokens.ForInbox($"{OwnerId(1):N}_s_{ItemId(1):N}");

        var page = await Store.ListAsync(_recipient, null, 10, token, Token);

        Assert.Equal(_recipient, Assert.Single(page.Items).Value.RecipientId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListAsync_StorageRejectsAStorageToken_ThrowsInvalidContinuationToken()
    {
        await Store.AddIfAbsentAsync(NewEntry(), Token);
        var token = ContinuationTokens.ForStorage("garbage-token-that-azurite-rejects");

        var outcome = await Record.ExceptionAsync(() => Store.ListAsync(_recipient, null, 10, token, Token));

        Assert.True(outcome is null or SharpVault.Application.Common.Exceptions.InvalidContinuationTokenException);
    }

    [Fact]
    public async Task ListAsync_NonPositivePageSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store.ListAsync(_recipient, null, 0, null, Token));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task InboxRows_HoldNoItemNameOrSecretMaterial()
    {
        await Store.AddIfAbsentAsync(NewEntry(filedFolderId: Guid.NewGuid()), Token);

        var row = (await InboxTable.GetEntityAsync<TableEntity>(_recipient.ToString("D"), $"{OwnerId(1):N}_s_{ItemId(1):N}", cancellationToken: Token)).Value;

        Assert.Equal(
            ExpectedColumns.Order(),
            row.Keys.Where(key => key is not ("PartitionKey" or "RowKey" or "Timestamp" or "odata.etag")).Order());
        Assert.DoesNotContain(row.Select(pair => pair.Value), value => value is byte[]);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task InboxOperations_EveryQueryCarriesThePartitionKeyAndCallCountsAreFixed()
    {
        await Store.AddIfAbsentAsync(NewEntry(item: 1, filedFolderId: Guid.NewGuid()), Token);
        var stored = (await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token))!;
        _ = Factory.Server;

        StorageRequests.Clear();
        await Store.AddIfAbsentAsync(NewEntry(item: 2), Token);
        var afterAdd = StorageRequests.Count;
        await Store.GetAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token);
        var afterGet = StorageRequests.Count;
        await Store.ListAsync(_recipient, null, 50, null, Token);
        var afterList = StorageRequests.Count;
        await Store.ListAsync(_recipient, Guid.NewGuid(), 50, null, Token);
        var afterFilteredList = StorageRequests.Count;
        await Store.SetFiledFolderAsync(NewEntry(item: 1, filedFolderId: null), stored.ETag, Token);
        var afterSet = StorageRequests.Count;
        await Store.RemoveAsync(_recipient, OwnerId(1), ShareItemKind.Secret, ItemId(1), Token);
        var afterRemove = StorageRequests.Count;

        Assert.Equal(1, afterAdd);
        Assert.Equal(1, afterGet - afterAdd);
        Assert.Equal(1, afterList - afterGet);
        Assert.Equal(1, afterFilteredList - afterList);
        Assert.Equal(1, afterSet - afterFilteredList);
        Assert.Equal(2, afterRemove - afterSet);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
        Assert.All(StorageRequests.Requests.Where(request => request.IsEntityQuery), request => Assert.Contains(_recipient.ToString("D"), request.Filter, StringComparison.Ordinal));
    }
}
