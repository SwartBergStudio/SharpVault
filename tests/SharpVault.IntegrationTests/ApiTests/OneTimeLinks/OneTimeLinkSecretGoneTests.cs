// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkSecretGoneTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    [Fact]
    public async Task Open_SecretEdited_StillOpens()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var (edit, _) = await OwnerVault.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, EditBody("Edited name"));
        using (edit)
        {
            Assert.True(edit.IsSuccessStatusCode, $"Edit failed with {edit.StatusCode}.");
        }

        using var opened = await OpenAsync(linkId);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Open_SecretRecycled_Returns404ThenOpensOnceRestored()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        await RecycleAsync(secret);

        using (var refused = await OpenAsync(linkId))
        {
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        var (restore, _) = await OwnerVault.RestoreRecycledSecretAsync(secret.Id);
        using (restore)
        {
            Assert.True(restore.IsSuccessStatusCode, $"Restore failed with {restore.StatusCode}.");
        }

        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Open_SecretPurged_Returns404()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        await RecycleAsync(secret);
        using (var purge = await OwnerVault.PurgeRecycledSecretAsync(secret.Id))
        {
            Assert.True(purge.IsSuccessStatusCode, $"Purge failed with {purge.StatusCode}.");
        }

        using var refused = await OpenAsync(linkId);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task Open_SecretInRecycledFolder_Returns404()
    {
        var folder = await NewFolderAsync("Folder " + Guid.NewGuid().ToString("N"));
        var secret = await NewSecretAsync(folderId: folder.Id);
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        using (var recycled = await OwnerVault.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents))
        {
            Assert.True(recycled.IsSuccessStatusCode, $"Folder delete failed with {recycled.StatusCode}.");
        }

        using var refused = await OpenAsync(linkId);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    private async Task RecycleAsync(SecretDto secret)
    {
        using var recycled = await OwnerVault.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);
        Assert.True(recycled.IsSuccessStatusCode, $"Recycle failed with {recycled.StatusCode}.");
    }
}
