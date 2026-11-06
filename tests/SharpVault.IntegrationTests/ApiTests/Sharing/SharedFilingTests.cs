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
public sealed class SharedFilingTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private VaultTestApi ViewerVault => ApiFor(TestUsers.ShareViewer);

    private SharingTestApi Viewer => SharingFor(TestUsers.ShareViewer);

    private async Task<FolderDto> ViewerFolderAsync(string name) =>
        Require(await ViewerVault.CreateFolderAsync(new CreateFolderRequestDto { Name = name }));

    private static FileSharedItemRequestDto Into(Guid? folderId) => new() { FolderId = folderId };

    [Fact]
    public async Task ListSharedItems_FiledFolderId_ReturnsOnlyFiled()
    {
        var filed = await NewSecretAsync();
        var loose = await NewSecretAsync();
        await ShareSecretAsync(filed.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(loose.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var folder = await ViewerFolderAsync("My filing");

        Assert.Equal(HttpStatusCode.NoContent, (await Viewer.FileSharedSecretAsync(OwnerId, filed.Id, Into(folder.Id))).StatusCode);

        var inFolder = Assert.Single(await WholeSharedListAsync(Viewer, folder.Id));
        Assert.Equal(filed.Id, inFolder.ItemId);
        Assert.Equal(folder.Id, inFolder.FiledFolderId);
        Assert.Equal(2, (await WholeSharedListAsync(Viewer)).Count);
        Assert.Null((await WholeSharedListAsync(Viewer)).Single(item => item.ItemId == loose.Id).FiledFolderId);
    }

    [Fact]
    public async Task Filing_Unfile_ClearsTheFiledFolder()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var folder = await ViewerFolderAsync("Temporary filing");
        await Viewer.FileSharedSecretAsync(OwnerId, secret.Id, Into(folder.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await Viewer.FileSharedSecretAsync(OwnerId, secret.Id, Into(null))).StatusCode);

        Assert.Null(Assert.Single(await WholeSharedListAsync(Viewer)).FiledFolderId);
        Assert.Empty(await WholeSharedListAsync(Viewer, folder.Id));
    }

    [Fact]
    public async Task Filing_SharedFolder_IsFiledLikeASecret()
    {
        var shared = await NewFolderAsync("Shared folder");
        await ShareFolderAsync(shared.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var folder = await ViewerFolderAsync("Folder for folders");

        Assert.Equal(HttpStatusCode.NoContent, (await Viewer.FileSharedFolderAsync(OwnerId, shared.Id, Into(folder.Id))).StatusCode);

        var filed = Assert.Single(await WholeSharedListAsync(Viewer, folder.Id));
        Assert.Equal(SharedItemKindDto.Folder, filed.Kind);
        Assert.Equal(shared.Id, filed.ItemId);
    }

    [Fact]
    public async Task Filing_RecipientFolderDeletedThenRestored_FilingReturns()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var folder = await ViewerFolderAsync("Recycled filing");
        await Viewer.FileSharedSecretAsync(OwnerId, secret.Id, Into(folder.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await ViewerVault.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        Require(await ViewerVault.RestoreRecycledFolderAsync(folder.Id));

        var filed = Assert.Single(await WholeSharedListAsync(Viewer, folder.Id));
        Assert.Equal(secret.Id, filed.ItemId);
    }

    [Fact]
    public async Task Filing_RevokeAndRegrant_StartsUnfiled()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var folder = await ViewerFolderAsync("Before revoke");
        await Viewer.FileSharedSecretAsync(OwnerId, secret.Id, Into(folder.Id));

        await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareViewer.ObjectId);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        Assert.Null(Assert.Single(await WholeSharedListAsync(Viewer)).FiledFolderId);
        Assert.Empty(await WholeSharedListAsync(Viewer, folder.Id));
    }

    [Fact]
    public async Task FileSharedItem_Revoked_Returns404()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var folder = await ViewerFolderAsync("After revoke");
        await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareViewer.ObjectId);

        var result = await Viewer.FileSharedSecretAsync(OwnerId, secret.Id, Into(folder.Id));

        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task FileSharedItem_OtherUsersFolderId_Returns404AndChangesNothing()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var foreign = await NewFolderAsync("Owner folder");

        var result = await Viewer.FileSharedSecretAsync(OwnerId, secret.Id, Into(foreign.Id));

        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Null(Assert.Single(await WholeSharedListAsync(Viewer)).FiledFolderId);
    }

    [Fact]
    public async Task FileSharedItem_DoesNotChangeAnyOwnerRow()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var folder = await ViewerFolderAsync("Recipient filing");
        var before = await VaultRows.ListPartitionAsync(VaultsTable, OwnerId, Token);

        await Viewer.FileSharedSecretAsync(OwnerId, secret.Id, Into(folder.Id));

        var after = await VaultRows.ListPartitionAsync(VaultsTable, OwnerId, Token);
        Assert.Equal(
            before.Select(row => $"{row.RowKey}|{row.ETag}").Order(StringComparer.Ordinal),
            after.Select(row => $"{row.RowKey}|{row.ETag}").Order(StringComparer.Ordinal));
    }
}
