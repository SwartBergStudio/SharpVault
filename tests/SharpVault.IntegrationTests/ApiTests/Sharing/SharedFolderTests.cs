// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
public sealed class SharedFolderTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private SharingTestApi Viewer => SharingFor(TestUsers.ShareViewer);

    [Fact]
    public async Task FolderShare_ListShowsFolder_DrillDownShowsSecrets()
    {
        var folder = await NewFolderAsync("Team folder");
        var first = await NewSecretAsync(folderId: folder.Id);
        var second = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var listed = Assert.Single(await WholeSharedListAsync(Viewer));
        Assert.Equal(SharedItemKindDto.Folder, listed.Kind);
        Assert.Equal(folder.Id, listed.ItemId);
        Assert.Equal("Team folder", listed.Name);

        var contents = Require(await Viewer.GetSharedFolderAsync(OwnerId, folder.Id));
        Assert.Equal("Team folder", contents.Name);
        Assert.Equal(OwnerId, contents.OwnerId);
        Assert.Equal(SharePermissionDto.View, contents.Permission);
        Assert.Equal(2, contents.Items.Count);
        Assert.Contains(contents.Items, item => item.Id == first.Id);
        Assert.Contains(contents.Items, item => item.Id == second.Id);
        Assert.All(contents.Items, item => Assert.Null(item.FolderId));
    }

    [Fact]
    public async Task SharedFolder_Contents_PageAcrossContinuationTokens()
    {
        var folder = await NewFolderAsync("Paged folder");
        var created = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            created.Add((await NewSecretAsync(folderId: folder.Id)).Id);
        }

        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var all = await WholeSharedFolderSecretsAsync(Viewer, OwnerId, folder.Id, 1);

        Assert.Equal(created.Order(), all.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task SharedFolder_PathStopsAtHighestGrantedAncestor()
    {
        var root = await NewFolderAsync("Ungranted root");
        var middle = await NewFolderAsync("Granted middle", root.Id);
        var leaf = await NewFolderAsync("Leaf", middle.Id);
        await ShareFolderAsync(middle.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var contents = Require(await Viewer.GetSharedFolderAsync(OwnerId, leaf.Id));

        Assert.Equal([middle.Id, leaf.Id], contents.Path.Select(step => step.FolderId));
        Assert.DoesNotContain(contents.Path, step => step.FolderId == root.Id);
        await AssertFolderStatusAsync(HttpStatusCode.NotFound, Viewer, root.Id);
    }

    [Fact]
    public async Task SharedFolder_SubFoldersHoldOnlyDirectChildren()
    {
        var root = await NewFolderAsync("Root");
        var child = await NewFolderAsync("Child", root.Id);
        var grandchild = await NewFolderAsync("Grandchild", child.Id);
        await ShareFolderAsync(root.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var contents = Require(await Viewer.GetSharedFolderAsync(OwnerId, root.Id));

        var subFolder = Assert.Single(contents.SubFolders);
        Assert.Equal(child.Id, subFolder.FolderId);
        Assert.DoesNotContain(contents.SubFolders, item => item.FolderId == grandchild.Id);
        Require(await Viewer.GetSharedFolderAsync(OwnerId, grandchild.Id));
    }

    [Fact]
    public async Task SharedFolder_SiblingFolderNamesNeverReturned()
    {
        var parent = await NewFolderAsync("Parent");
        var shared = await NewFolderAsync("Shared child", parent.Id);
        var sibling = await NewFolderAsync("Secret sibling name", parent.Id);
        await ShareFolderAsync(shared.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var result = await Viewer.GetSharedFolderAsync(OwnerId, shared.Id);
        var text = await result.Response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.DoesNotContain("Secret sibling name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Parent", text, StringComparison.Ordinal);
        await AssertFolderStatusAsync(HttpStatusCode.NotFound, Viewer, sibling.Id);
        await AssertFolderStatusAsync(HttpStatusCode.NotFound, Viewer, parent.Id);
    }

    [Fact]
    public async Task SharedFolder_EditorOnFolder_CanEditContainedSecretButViewerCannot()
    {
        var folder = await NewFolderAsync("Edit folder");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareFolderAsync(folder.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        var editor = SharingFor(TestUsers.ShareEditor);

        var viewerOpened = Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        var viewerEdit = await Viewer.UpdateSharedSecretAsync(OwnerId, secret.Id, viewerOpened.Item.ConcurrencyToken, EditBody("Viewer rename"));
        Assert.Equal(HttpStatusCode.NotFound, viewerEdit.Response.StatusCode);

        var editorOpened = Require(await editor.GetSharedSecretAsync(OwnerId, secret.Id));
        var edited = Require(await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, editorOpened.Item.ConcurrencyToken, EditBody("Editor rename")));
        Assert.Equal("Editor rename", edited.Name);
        Assert.Null(edited.FolderId);
        Assert.Equal(folder.Id, Require(await OwnerVault.GetSecretAsync(secret.Id)).FolderId);
    }

    [Fact]
    public async Task SharedFolder_ManagerOnFolder_CanShareContainedSecret()
    {
        var folder = await NewFolderAsync("Manage folder");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.FolderManager, SharePermissionDto.Manage);

        var created = await SharingFor(TestUsers.FolderManager).CreateSecretGrantAsync(
            OwnerId,
            secret.Id,
            GrantBody(TestUsers.ShareViewer, SharePermissionDto.View));

        Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
    }

    [Fact]
    public async Task FolderShare_ListShowsFolderGrantsWithGrantedBy()
    {
        var folder = await NewFolderAsync("Grant list folder");
        await ShareFolderAsync(folder.Id, TestUsers.FolderManager, SharePermissionDto.Manage);
        Require(await SharingFor(TestUsers.FolderManager).CreateFolderGrantAsync(OwnerId, folder.Id, GrantBody(TestUsers.FolderViewer, SharePermissionDto.View)));

        var grants = Require(await OwnerSharing.ListFolderGrantsAsync(OwnerId, folder.Id));

        Assert.Equal(2, grants.Items.Count);
        var delegated = Assert.Single(grants.Items, item => item.RecipientId == TestUsers.FolderViewer.ObjectId);
        Assert.Equal(TestUsers.FolderManager.DisplayName, delegated.GrantedByDisplayName);
    }
}
