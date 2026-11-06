// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class FolderEndpointsTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private async Task<IReadOnlyList<(Guid Id, Guid? ParentId, string? Name)>> TreeAsync() =>
        [.. Require(await Api.ListFoldersAsync()).Items.OrderBy(folder => folder.Id).Select(folder => (folder.Id, folder.ParentId, folder.Name))];

    [Fact]
    public async Task CreateFolder_RootAndNested_ReturnTree()
    {
        var root = await NewFolderAsync("Root");
        var response = await Api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Nested", ParentId = root.Id });

        Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
        var nested = response.Body!;
        Assert.EndsWith($"/api/vault/folders/{nested.Id}", response.Response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Null(root.ParentId);
        Assert.Equal(root.Id, nested.ParentId);
        var listed = Require(await Api.ListFoldersAsync()).Items;
        Assert.Equal(2, listed.Count);
        Assert.Equal(root.Id, listed.Single(folder => folder.Name == "Nested").ParentId);
        Assert.Null(listed.Single(folder => folder.Name == "Root").ParentId);
    }

    [Fact]
    public async Task ListFolders_SortedByName()
    {
        await NewFolderAsync("banana");
        await NewFolderAsync("Cherry");
        await NewFolderAsync("apple");
        await NewFolderAsync("Banana split");

        var names = Require(await Api.ListFoldersAsync()).Items.Select(folder => folder.Name);

        Assert.Equal(["apple", "banana", "Banana split", "Cherry"], names);
    }

    [Fact]
    public async Task RenameFolder_UpdatesName()
    {
        var folder = await NewFolderAsync("Before");

        var renamed = Require(await Api.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "After" }));

        Assert.Equal("After", renamed.Name);
        Assert.Equal(folder.Id, renamed.Id);
        Assert.Equal(folder.ParentId, renamed.ParentId);
        Assert.Equal("After", Assert.Single(Require(await Api.ListFoldersAsync()).Items).Name);
    }

    [Fact]
    public async Task MoveFolder_ToOtherParent_Succeeds()
    {
        var target = await NewFolderAsync("Target");
        var moving = await NewFolderAsync("Moving");
        var child = await NewFolderAsync("Child", moving.Id);

        var moved = Require(await Api.MoveFolderAsync(moving.Id, new MoveFolderRequestDto { ParentId = target.Id }));

        Assert.Equal(target.Id, moved.ParentId);
        var listed = Require(await Api.ListFoldersAsync()).Items;
        Assert.Equal(target.Id, listed.Single(folder => folder.Id == moving.Id).ParentId);
        Assert.Equal(moving.Id, listed.Single(folder => folder.Id == child.Id).ParentId);
    }

    [Fact]
    public async Task MoveFolder_ToRoot_Succeeds()
    {
        var parent = await NewFolderAsync("Parent");
        var child = await NewFolderAsync("Child", parent.Id);

        var moved = Require(await Api.MoveFolderAsync(child.Id, new MoveFolderRequestDto { ParentId = null }));

        Assert.Null(moved.ParentId);
        Assert.Null(Require(await Api.ListFoldersAsync()).Items.Single(folder => folder.Id == child.Id).ParentId);
    }

    [Fact]
    public async Task MoveFolder_IntoItself_Returns400()
    {
        var folder = await NewFolderAsync("Self");
        var before = await TreeAsync();

        var response = await Api.MoveFolderAsync(folder.Id, new MoveFolderRequestDto { ParentId = folder.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.Response.StatusCode);
        Assert.Equal(before, await TreeAsync());
    }

    [Fact]
    public async Task MoveFolder_IntoOwnDescendant_Returns400AndTreeUnchanged()
    {
        var top = await NewFolderAsync("Top");
        var middle = await NewFolderAsync("Middle", top.Id);
        var bottom = await NewFolderAsync("Bottom", middle.Id);
        var before = await TreeAsync();

        var response = await Api.MoveFolderAsync(top.Id, new MoveFolderRequestDto { ParentId = bottom.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.Response.StatusCode);
        Assert.Equal(before, await TreeAsync());
    }

    [Fact]
    public async Task CreateFolder_AtDepthTen_Returns400()
    {
        var chain = await NewFolderChainAsync(MaxFolderDepth);

        var response = await Api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Too deep", ParentId = chain[^1].Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.Response.StatusCode);
        Assert.Equal(MaxFolderDepth, Require(await Api.ListFoldersAsync()).Items.Count);
    }

    [Fact]
    public async Task CreateFolder_AtDepthNine_Succeeds()
    {
        var chain = await NewFolderChainAsync(MaxFolderDepth - 1);

        var response = await Api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Deepest", ParentId = chain[^1].Id });

        Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
        Assert.Equal(MaxFolderDepth, Require(await Api.ListFoldersAsync()).Items.Count);
    }

    [Fact]
    public async Task MoveFolder_SubtreeWouldExceedMaxDepth_Returns400()
    {
        var target = await NewFolderChainAsync(5);
        var subtree = await NewFolderChainAsync(6);
        var before = await TreeAsync();

        var response = await Api.MoveFolderAsync(subtree[0].Id, new MoveFolderRequestDto { ParentId = target[^1].Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.Response.StatusCode);
        Assert.Equal(before, await TreeAsync());
    }

    [Fact]
    public async Task MoveFolder_SubtreeFitsExactlyAtMaxDepth_Succeeds()
    {
        var target = await NewFolderChainAsync(5);
        var subtree = await NewFolderChainAsync(5);

        var response = await Api.MoveFolderAsync(subtree[0].Id, new MoveFolderRequestDto { ParentId = target[^1].Id });

        Assert.Equal(HttpStatusCode.OK, response.Response.StatusCode);
    }

    [Fact]
    public async Task RestoreFolder_OriginalParentNowTooDeep_RestoresToRoot()
    {
        var anchor = await NewFolderAsync("Anchor");
        var child = await NewFolderAsync("Child", anchor.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Api.DeleteFolderAsync(child.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        var chain = await NewFolderChainAsync(MaxFolderDepth - 1);
        Require(await Api.MoveFolderAsync(anchor.Id, new MoveFolderRequestDto { ParentId = chain[^1].Id }));

        var restored = Require(await Api.RestoreRecycledFolderAsync(child.Id));

        Assert.Null(restored.ParentId);
        Assert.Null(Require(await Api.ListFoldersAsync()).Items.Single(folder => folder.Id == child.Id).ParentId);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a#b")]
    [InlineData("a?b")]
    [InlineData("/\\#?")]
    public async Task CreateFolder_NameWithSlashHashQuestionBackslash_Succeeds(string name)
    {
        var response = await Api.CreateFolderAsync(new CreateFolderRequestDto { Name = name });

        Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
        Assert.Equal(name, response.Body!.Name);
        Assert.Equal(name, Assert.Single(Require(await Api.ListFoldersAsync()).Items).Name);
        var renamed = await Api.RenameFolderAsync(response.Body.Id, new RenameFolderRequestDto { Name = name + name });
        Assert.Equal(HttpStatusCode.OK, renamed.Response.StatusCode);
    }
}
