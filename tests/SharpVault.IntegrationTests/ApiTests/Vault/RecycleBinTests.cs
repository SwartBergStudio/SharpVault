// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class RecycleBinTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private async Task DeleteSecretAsync(SecretDto secret) =>
        Assert.Equal(HttpStatusCode.NoContent, (await Api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);

    private async Task DeleteFolderWithContentsAsync(FolderDto folder) =>
        Assert.Equal(HttpStatusCode.NoContent, (await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);

    [Fact]
    public async Task RecycleBin_ListsFoldersThenSecretsWithPurgeAfter()
    {
        var secret = await NewSecretAsync();
        var folder = await NewFolderAsync("Binned folder");
        await DeleteSecretAsync(secret);
        await DeleteFolderWithContentsAsync(folder);

        var page = Require(await Api.ListRecycleBinAsync());

        Assert.Equal(2, page.Items.Count);
        var binnedFolder = page.Items[0];
        var binnedSecret = page.Items[1];
        Assert.Equal(RecycleBinItemKindDto.Folder, binnedFolder.Kind);
        Assert.Equal(folder.Id, binnedFolder.Id);
        Assert.Equal("Binned folder", binnedFolder.Name);
        Assert.Null(binnedFolder.Type);
        Assert.Equal(RecycleBinItemKindDto.Secret, binnedSecret.Kind);
        Assert.Equal(secret.Id, binnedSecret.Id);
        Assert.Equal(secret.Name, binnedSecret.Name);
        Assert.Equal(SecretTypeDto.Login, binnedSecret.Type);
        Assert.All(page.Items, item =>
        {
            Assert.Equal(item.DeletedAt + VaultLimits.RecycleBinRetention, item.PurgeAfter);
            Assert.False(item.IsDamaged);
        });
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task RecycleBin_Paging_WithToken()
    {
        var expected = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            var folder = await NewFolderAsync($"Folder {index}");
            await DeleteFolderWithContentsAsync(folder);
            expected.Add(folder.Id);
        }

        for (var index = 0; index < 3; index++)
        {
            var secret = await NewSecretAsync();
            await DeleteSecretAsync(secret);
            expected.Add(secret.Id);
        }

        var first = Require(await Api.ListRecycleBinAsync(pageSize: 2));
        Assert.NotNull(first.ContinuationToken);
        var second = Require(await Api.ListRecycleBinAsync(pageSize: 2, continuationToken: first.ContinuationToken));
        Assert.NotNull(second.ContinuationToken);
        var third = Require(await Api.ListRecycleBinAsync(pageSize: 2, continuationToken: second.ContinuationToken));

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Single(third.Items);
        Assert.Null(third.ContinuationToken);
        var all = first.Items.Concat(second.Items).Concat(third.Items).ToList();
        Assert.Equal(expected.Order(), all.Select(item => item.Id).Order());
        Assert.Equal(2, all.Take(2).Count(item => item.Kind == RecycleBinItemKindDto.Folder));
    }

    [Fact]
    public async Task RestoreSecret_ToOriginalFolder()
    {
        var folder = await NewFolderAsync("Home");
        var secret = await NewSecretAsync(folder.Id);
        await DeleteSecretAsync(secret);

        var restored = Require(await Api.RestoreRecycledSecretAsync(secret.Id));

        Assert.Equal(folder.Id, restored.FolderId);
        Assert.Equal(secret.Name, restored.Name);
        Assert.Equal(folder.Id, Require(await Api.GetSecretAsync(secret.Id)).FolderId);
        Assert.Empty(await WholeBinAsync());
    }

    [Fact]
    public async Task RestoreSecret_OriginalFolderDeleted_ToRoot()
    {
        var folder = await NewFolderAsync("Gone");
        var secret = await NewSecretAsync(folder.Id);
        await DeleteSecretAsync(secret);
        Assert.Equal(HttpStatusCode.NoContent, (await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.MoveToParent)).StatusCode);

        var restored = Require(await Api.RestoreRecycledSecretAsync(secret.Id));

        Assert.Null(restored.FolderId);
        Assert.Equal(secret.Id, Assert.Single(await WholeListAsync(unfiled: true)).Id);
        Assert.Equal(folder.Id, Assert.Single(await WholeBinAsync()).Id);
    }

    [Fact]
    public async Task RestoreFolder_RestoresBatch()
    {
        var folder = await NewFolderAsync("Top");
        var child = await NewFolderAsync("Child", folder.Id);
        var inTop = await NewSecretAsync(folder.Id);
        var inChild = await NewSecretAsync(child.Id);
        await DeleteFolderWithContentsAsync(folder);

        var restored = Require(await Api.RestoreRecycledFolderAsync(folder.Id));

        Assert.Equal(folder.Id, restored.Id);
        Assert.Null(restored.ParentId);
        Assert.Equal("Top", restored.Name);
        var folders = Require(await Api.ListFoldersAsync()).Items;
        Assert.Equal(2, folders.Count);
        Assert.Equal(folder.Id, folders.Single(item => item.Id == child.Id).ParentId);
        Assert.Equal(folder.Id, Require(await Api.GetSecretAsync(inTop.Id)).FolderId);
        Assert.Equal(child.Id, Require(await Api.GetSecretAsync(inChild.Id)).FolderId);
        Assert.Empty(await WholeBinAsync());
    }

    [Fact]
    public async Task RestoreFolder_SubfolderOfBatch_RestoresItsSubtreeToRoot()
    {
        var top = await NewFolderAsync("Top");
        var middle = await NewFolderAsync("Middle", top.Id);
        var bottom = await NewFolderAsync("Bottom", middle.Id);
        var inTop = await NewSecretAsync(top.Id);
        var inMiddle = await NewSecretAsync(middle.Id);
        var inBottom = await NewSecretAsync(bottom.Id);
        await DeleteFolderWithContentsAsync(top);

        var restored = Require(await Api.RestoreRecycledFolderAsync(middle.Id));

        Assert.Equal(middle.Id, restored.Id);
        Assert.Null(restored.ParentId);
        var folders = Require(await Api.ListFoldersAsync()).Items;
        Assert.Equal(2, folders.Count);
        Assert.Equal(middle.Id, folders.Single(item => item.Id == bottom.Id).ParentId);
        Assert.Equal(middle.Id, Require(await Api.GetSecretAsync(inMiddle.Id)).FolderId);
        Assert.Equal(bottom.Id, Require(await Api.GetSecretAsync(inBottom.Id)).FolderId);
        var remaining = await WholeBinAsync();
        Assert.Equal(new[] { top.Id, inTop.Id }.Order(), remaining.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task RestoreFolder_ParentNowTooDeep_ToRoot()
    {
        var anchor = await NewFolderAsync("Anchor");
        var folder = await NewFolderAsync("Restored", anchor.Id);
        var nested = await NewFolderAsync("Nested", folder.Id);
        await DeleteFolderWithContentsAsync(folder);
        var chain = await NewFolderChainAsync(MaxFolderDepth - 2);
        Require(await Api.MoveFolderAsync(anchor.Id, new MoveFolderRequestDto { ParentId = chain[^1].Id }));

        var restored = Require(await Api.RestoreRecycledFolderAsync(folder.Id));

        Assert.Null(restored.ParentId);
        var folders = Require(await Api.ListFoldersAsync()).Items;
        Assert.Null(folders.Single(item => item.Id == folder.Id).ParentId);
        Assert.Equal(folder.Id, folders.Single(item => item.Id == nested.Id).ParentId);
    }

    [Fact]
    public async Task PurgeEndpoint_Secret_DeletesBinRowAndVersions()
    {
        var secret = await NewSecretAsync();
        secret = await ReplaceValueAsync(secret, "second");
        secret = await ReplaceValueAsync(secret, "third");
        Assert.Equal(2, Require(await Api.ListHistoryAsync(secret.Id)).Items.Count);
        await DeleteSecretAsync(secret);

        var purged = await Api.PurgeRecycledSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.NoContent, purged.StatusCode);
        Assert.Empty(await WholeBinAsync());
        var rows = await VaultRowsAsync();
        Assert.DoesNotContain(rows, row => row.RowKey.Contains(secret.Id.ToString("N"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task PurgeEndpoint_Folder_PurgesSubtree()
    {
        var folder = await NewFolderAsync("Top");
        var child = await NewFolderAsync("Child", folder.Id);
        var secret = await NewSecretAsync(child.Id);
        await ReplaceValueAsync(secret, "second");
        var survivor = await NewSecretAsync();
        await DeleteFolderWithContentsAsync(folder);

        var purged = await Api.PurgeRecycledFolderAsync(folder.Id);

        Assert.Equal(HttpStatusCode.NoContent, purged.StatusCode);
        Assert.Empty(await WholeBinAsync());
        var rows = await VaultRowsAsync();
        foreach (var id in new[] { folder.Id, child.Id, secret.Id })
        {
            Assert.DoesNotContain(rows, row => row.RowKey.Contains(id.ToString("N"), StringComparison.Ordinal));
        }

        Assert.Equal(survivor.Id, Assert.Single(await WholeListAsync()).Id);
    }

    [Fact]
    public async Task RestoreAfterPurge_Returns404()
    {
        var secret = await NewSecretAsync();
        var folder = await NewFolderAsync("Folder");
        await DeleteSecretAsync(secret);
        await DeleteFolderWithContentsAsync(folder);
        Assert.Equal(HttpStatusCode.NoContent, (await Api.PurgeRecycledSecretAsync(secret.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Api.PurgeRecycledFolderAsync(folder.Id)).StatusCode);

        var restoredSecret = await Api.RestoreRecycledSecretAsync(secret.Id);
        var restoredFolder = await Api.RestoreRecycledFolderAsync(folder.Id);
        var purgedAgain = await Api.PurgeRecycledSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.NotFound, restoredSecret.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, restoredFolder.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, purgedAgain.StatusCode);
        Assert.Empty(await WholeListAsync());
        Assert.Empty(Require(await Api.ListFoldersAsync()).Items);
    }
}
