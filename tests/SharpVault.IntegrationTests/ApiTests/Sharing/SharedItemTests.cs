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
public sealed class SharedItemTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private SharingTestApi Viewer => SharingFor(TestUsers.ShareViewer);

    [Fact]
    public async Task ListSharedItems_NothingShared_ReturnsEmptyPage()
    {
        var page = Require(await Viewer.ListSharedItemsAsync());

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListSharedItems_SecretAndFolder_ListsBothWithOwnerDetails()
    {
        var secret = await NewSecretAsync();
        var folder = await NewFolderAsync("Shared folder");
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.Edit);

        var items = await WholeSharedListAsync(Viewer);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(TestUsers.User.DisplayName, item.OwnerDisplayName));
        Assert.Contains(items, item => item.Kind == SharedItemKindDto.Secret && item.ItemId == secret.Id && item.Permission == SharePermissionDto.View);
        Assert.Contains(items, item => item.Kind == SharedItemKindDto.Folder && item.ItemId == folder.Id && item.Permission == SharePermissionDto.Edit);
    }

    [Fact]
    public async Task ListSharedItems_GroupsByOwnerAcrossPages()
    {
        var otherApi = ApiFor(TestUsers.OtherUser);
        var otherSharing = new SharingTestApi(otherApi.Client);
        for (var index = 0; index < 2; index++)
        {
            var mine = await NewSecretAsync();
            await ShareSecretAsync(mine.Id, TestUsers.ShareViewer, SharePermissionDto.View);
            var theirs = await CreateSecretAsync(otherApi, new PlaintextMarkers());
            Require(await otherSharing.CreateSecretGrantAsync(TestUsers.OtherUser.ObjectId, theirs.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View)));
        }

        var collected = new List<SharedItemDto>();
        string? token = null;
        var pages = 0;
        do
        {
            var page = Require(await Viewer.ListSharedItemsAsync(1, token));
            collected.AddRange(page.Items);
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < 20);

        Assert.Equal(4, collected.Count);
        Assert.Equal(4, collected.Select(item => item.ItemId).Distinct().Count());
        var owners = collected.Select(item => item.OwnerId).ToList();
        var changes = owners.Zip(owners.Skip(1), (left, right) => left != right).Count(different => different);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task GetSharedSecret_Viewer_ReturnsDetailWithoutValue()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var result = await Viewer.GetSharedSecretAsync(OwnerId, secret.Id);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        var body = Assert.IsType<SharedSecretDto>(result.Body);
        Assert.Equal(markers.Name, body.Item.Name);
        Assert.Equal(markers.Username, body.Item.Username);
        Assert.True(body.Item.HasValue);
        Assert.Equal(OwnerId, body.OwnerId);
        Assert.Equal(TestUsers.User.DisplayName, body.OwnerDisplayName);
        var text = await result.Response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(markers.Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain(markers.Notes, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevealSharedNotes_Viewer_ReturnsNotes()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var notes = Require(await Viewer.RevealSharedSecretAsync(
            OwnerId,
            secret.Id,
            new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View }));

        Assert.Equal(markers.Notes, notes.Value);
    }

    [Fact]
    public async Task SharedUpdate_Viewer_Returns404AndChangesNothing()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var opened = Require(await Viewer.GetSharedSecretAsync(OwnerId, secret.Id));

        var result = await Viewer.UpdateSharedSecretAsync(OwnerId, secret.Id, opened.Item.ConcurrencyToken, EditBody("Viewer rename", "viewer-value"));

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(markers.Name, Require(await OwnerVault.GetSecretAsync(secret.Id)).Name);
        Assert.Equal(markers.Value, await OwnerRevealValueAsync(secret.Id));
    }

    [Fact]
    public async Task SharedUpdate_Editor_KeepsTagsAndNotesWhenNotReplaced()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        var editor = SharingFor(TestUsers.ShareEditor);
        var opened = Require(await editor.GetSharedSecretAsync(OwnerId, secret.Id));
        var body = EditBody("Renamed by editor");
        body.Tags = [.. opened.Item.Tags];

        Require(await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, opened.Item.ConcurrencyToken, body));

        var ownerView = Require(await OwnerVault.GetSecretAsync(secret.Id));
        Assert.Equal("Renamed by editor", ownerView.Name);
        Assert.Equal(markers.Value, await OwnerRevealValueAsync(secret.Id));
        Assert.Equal(markers.Notes, Require(await OwnerVault.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View })).Value);
    }

    [Fact]
    public async Task SharedUpdate_NoIfMatch_Returns400()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);

        var result = await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(OwnerId, secret.Id, null, EditBody("No token"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
    }
}
