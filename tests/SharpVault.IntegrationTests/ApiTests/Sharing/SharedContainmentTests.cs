// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
public sealed class SharedContainmentTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private const int DeepestChainLength = 10;

    private SharingTestApi Viewer => SharingFor(TestUsers.ShareViewer);

    private async Task DeleteSecretAsync(SecretDto secret) =>
        Assert.Equal(HttpStatusCode.NoContent, (await OwnerVault.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);

    private async Task DeleteFolderAsync(Guid folderId, FolderDeleteModeDto mode) =>
        Assert.Equal(HttpStatusCode.NoContent, (await OwnerVault.DeleteFolderAsync(folderId, mode)).StatusCode);

    private async Task MoveSecretAsync(SecretDto secret, Guid? folderId)
    {
        var current = Require(await OwnerVault.GetSecretAsync(secret.Id));
        Require(await OwnerVault.MoveSecretAsync(secret.Id, current.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folderId }));
    }

    [Fact]
    public async Task DeleteSecret_RecipientGets404AtOnce()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));

        await DeleteSecretAsync(secret);

        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
        Assert.Empty(await WholeSharedListAsync(Viewer));
        var reveal = await Viewer.RevealSharedSecretAsync(OwnerId, secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        Assert.Equal(HttpStatusCode.NotFound, reveal.Response.StatusCode);
    }

    [Fact]
    public async Task Restore_RecipientRegainsDirectAccess()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await DeleteSecretAsync(secret);

        Require(await OwnerVault.RestoreRecycledSecretAsync(secret.Id));

        Assert.Equal(markers.Value, await RevealValueAsync(Viewer, secret.Id));
        Assert.Single(await WholeSharedListAsync(Viewer));
    }

    [Fact]
    public async Task PurgeSecret_RecipientGets404AndNotListed()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await DeleteSecretAsync(secret);

        Assert.Equal(HttpStatusCode.NoContent, (await OwnerVault.PurgeRecycledSecretAsync(secret.Id)).StatusCode);

        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
        Assert.Empty(await WholeSharedListAsync(Viewer));
    }

    [Fact]
    public async Task DeleteFolder_RecipientLosesAccessAtOnceAndRestoreBringsItBack()
    {
        var folder = await NewFolderAsync("Folder to delete");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));

        await DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        await AssertFolderStatusAsync(HttpStatusCode.NotFound, Viewer, folder.Id);
        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
        Assert.Empty(await WholeSharedListAsync(Viewer));

        Require(await OwnerVault.RestoreRecycledFolderAsync(folder.Id));

        Require(await Viewer.GetSharedFolderAsync(OwnerId, folder.Id));
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
    }

    [Fact]
    public async Task DeleteFolderMoveToParent_RecipientLosesContents()
    {
        var folder = await NewFolderAsync("Folder to dissolve");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        await DeleteFolderAsync(folder.Id, FolderDeleteModeDto.MoveToParent);

        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
        await AssertFolderStatusAsync(HttpStatusCode.NotFound, Viewer, folder.Id);
        Assert.Equal(HttpStatusCode.OK, (await OwnerVault.GetSecretAsync(secret.Id)).Response.StatusCode);
    }

    [Fact]
    public async Task DeleteFolderMoveToParent_ParentAlsoShared_RecipientKeepsContents()
    {
        var parent = await NewFolderAsync("Shared parent");
        var child = await NewFolderAsync("Doomed child", parent.Id);
        var secret = await NewSecretAsync(folderId: child.Id);
        await ShareFolderAsync(parent.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));

        await DeleteFolderAsync(child.Id, FolderDeleteModeDto.MoveToParent);

        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        var contents = await WholeSharedFolderSecretsAsync(Viewer, OwnerId, parent.Id);
        Assert.Contains(contents, item => item.Id == secret.Id);
    }

    [Fact]
    public async Task DeletedParent_NestedSecretOfRestoredChild_GivesNoAccessFromParentGrant()
    {
        var parent = await NewFolderAsync("Recycled parent");
        var child = await NewFolderAsync("Restored child", parent.Id);
        var secret = await NewSecretAsync(folderId: child.Id);
        await ShareFolderAsync(parent.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await DeleteFolderAsync(parent.Id, FolderDeleteModeDto.DeleteContents);

        Assert.True((await OwnerVault.RestoreRecycledFolderAsync(child.Id)).Response.IsSuccessStatusCode);

        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
        await AssertFolderStatusAsync(HttpStatusCode.NotFound, Viewer, parent.Id);
    }

    [Fact]
    public async Task FolderGrantOnRecycledFolder_InterimRestoredSecret_Returns404()
    {
        var folder = await NewFolderAsync("Recycled folder");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        Assert.True((await OwnerVault.RestoreRecycledSecretAsync(secret.Id)).Response.IsSuccessStatusCode);

        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
    }

    [Fact]
    public async Task RestoreToRoot_RecipientKeepsDirectLosesFolderAccess()
    {
        var folder = await NewFolderAsync("Folder removed before restore");
        var direct = await NewSecretAsync(folderId: folder.Id);
        var viaFolder = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(direct.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await DeleteSecretAsync(direct);
        await DeleteSecretAsync(viaFolder);
        await DeleteFolderAsync(folder.Id, FolderDeleteModeDto.MoveToParent);

        var restoredDirect = Require(await OwnerVault.RestoreRecycledSecretAsync(direct.Id));
        var restoredViaFolder = Require(await OwnerVault.RestoreRecycledSecretAsync(viaFolder.Id));

        Assert.Null(restoredDirect.FolderId);
        Assert.Null(restoredViaFolder.FolderId);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, direct.Id));
        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, viaFolder.Id);
    }

    [Fact]
    public async Task MoveSecretOutOfSharedFolder_RecipientLosesAccess()
    {
        var folder = await NewFolderAsync("Shared folder");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));

        await MoveSecretAsync(secret, null);

        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
        Assert.DoesNotContain(await WholeSharedFolderSecretsAsync(Viewer, OwnerId, folder.Id), item => item.Id == secret.Id);
    }

    [Fact]
    public async Task MoveSecretIntoSharedFolder_RecipientGainsAccess()
    {
        var folder = await NewFolderAsync("Shared folder");
        var secret = await NewSecretAsync();
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);

        await MoveSecretAsync(secret, folder.Id);

        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        Assert.Contains(await WholeSharedFolderSecretsAsync(Viewer, OwnerId, folder.Id), item => item.Id == secret.Id);
    }

    [Fact]
    public async Task MoveFolderOutOfSharedFolder_RecipientLosesNestedSecrets()
    {
        var shared = await NewFolderAsync("Shared root");
        var nested = await NewFolderAsync("Nested", shared.Id);
        var secret = await NewSecretAsync(folderId: nested.Id);
        await ShareFolderAsync(shared.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));

        Require(await OwnerVault.MoveFolderAsync(nested.Id, new MoveFolderRequestDto { ParentId = null }));

        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);
        await AssertFolderStatusAsync(HttpStatusCode.NotFound, Viewer, nested.Id);
    }

    [Fact]
    public async Task MoveFolderIntoSharedFolder_RecipientGainsNestedSecrets()
    {
        var shared = await NewFolderAsync("Shared root");
        var loose = await NewFolderAsync("Loose");
        var secret = await NewSecretAsync(folderId: loose.Id);
        await ShareFolderAsync(shared.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await AssertSecretStatusAsync(HttpStatusCode.NotFound, Viewer, secret.Id);

        Require(await OwnerVault.MoveFolderAsync(loose.Id, new MoveFolderRequestDto { ParentId = shared.Id }));

        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        var contents = Require(await Viewer.GetSharedFolderAsync(OwnerId, shared.Id));
        Assert.Contains(contents.SubFolders, item => item.FolderId == loose.Id);
    }

    [Fact]
    public async Task MoveSecret_DirectGrantUnaffected()
    {
        var folder = await NewFolderAsync("Any folder");
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        await MoveSecretAsync(secret, folder.Id);

        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        await MoveSecretAsync(secret, null);
        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
    }

    [Fact]
    public async Task SecretAddedAfterFolderShare_RecipientSeesIt()
    {
        var folder = await NewFolderAsync("Shared early");
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var secret = await NewSecretAsync(folderId: folder.Id);

        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        Assert.Contains(await WholeSharedFolderSecretsAsync(Viewer, OwnerId, folder.Id), item => item.Id == secret.Id);
    }

    [Fact]
    public async Task SecretInNestedFolder_RecipientSeesIt()
    {
        var chain = new List<FolderDto>();
        for (var level = 0; level < DeepestChainLength; level++)
        {
            chain.Add(await NewFolderAsync($"Level {level + 1}", chain.Count == 0 ? null : chain[^1].Id));
        }

        var secret = await NewSecretAsync(folderId: chain[^1].Id);
        await ShareFolderAsync(chain[0].Id, TestUsers.ShareViewer, SharePermissionDto.View);

        Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        var deepest = Require(await Viewer.GetSharedFolderAsync(OwnerId, chain[^1].Id));
        Assert.Equal(DeepestChainLength, deepest.Path.Count);
        Assert.Equal(chain[0].Id, deepest.Path[0].FolderId);
    }

    [Fact]
    public async Task RenameFolder_GrantUnaffectedAndRecipientReadsNewName()
    {
        var folder = await NewFolderAsync("Before rename");
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        Require(await OwnerVault.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "After rename" }));

        Assert.Equal("After rename", Require(await Viewer.GetSharedFolderAsync(OwnerId, folder.Id)).Name);
        Assert.Equal("After rename", Assert.Single(await WholeSharedListAsync(Viewer)).Name);
    }
}
