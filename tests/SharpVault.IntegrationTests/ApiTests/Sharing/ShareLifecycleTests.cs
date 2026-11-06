// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.RegularExpressions;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
public sealed partial class ShareLifecycleTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    [Fact]
    public async Task FullLifecycle_ShareRevealEditChangeRevoke_MatchesEveryRequirement()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        var editor = SharingFor(TestUsers.ShareEditor);

        var listed = Assert.Single(await WholeSharedListAsync(editor));
        Assert.Equal(secret.Id, listed.ItemId);
        Assert.Equal(OwnerId, listed.OwnerId);
        Assert.Equal(SharedItemKindDto.Secret, listed.Kind);
        Assert.Equal(SharePermissionDto.Edit, listed.Permission);

        var opened = Require(await editor.GetSharedSecretAsync(OwnerId, secret.Id));
        Assert.Equal(markers.Name, opened.Item.Name);
        Assert.Null(opened.Item.FolderId);
        Assert.Equal(SharePermissionDto.Edit, opened.Permission);
        Assert.Equal(markers.Value, await RevealValueAsync(editor, secret.Id));

        var edited = Require(await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, opened.Item.ConcurrencyToken, EditBody("Edited by editor", "edited-value")));
        Assert.Null(edited.FolderId);
        Assert.Equal("edited-value", await OwnerRevealValueAsync(secret.Id));

        var changed = Require(await OwnerSharing.ChangeSecretGrantAsync(
            OwnerId,
            secret.Id,
            TestUsers.ShareEditor.ObjectId,
            grant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.View }));
        Assert.Equal(SharePermissionDto.View, changed.Permission);

        var refused = await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, edited.ConcurrencyToken, EditBody("Should not apply"));
        Assert.Equal(HttpStatusCode.NotFound, refused.Response.StatusCode);
        Assert.Equal("edited-value", await RevealValueAsync(editor, secret.Id));

        var revoked = await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareEditor.ObjectId);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        await AssertSecretStatusAsync(HttpStatusCode.NotFound, editor, secret.Id);
        Assert.Empty(await WholeSharedListAsync(editor));

        var revealAfter = await editor.RevealSharedSecretAsync(OwnerId, secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        Assert.Equal(HttpStatusCode.NotFound, revealAfter.Response.StatusCode);

        var audit = await VaultAuditRowsAsync(AuditEventType.SecretShared, AuditEventType.SecretShareChanged, AuditEventType.SecretShareRevoked);
        Assert.Equal(3, audit.Count);
    }

    [Fact]
    public async Task SharedEdit_OwnerSeesNewValueAndHistoryVersion()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        var editor = SharingFor(TestUsers.ShareEditor);
        var opened = Require(await editor.GetSharedSecretAsync(OwnerId, secret.Id));

        Require(await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, opened.Item.ConcurrencyToken, EditBody(markers.Name, "recipient-value")));

        Assert.Equal("recipient-value", await OwnerRevealValueAsync(secret.Id));
        var history = Require(await OwnerVault.ListHistoryAsync(secret.Id));
        var version = Assert.Single(history.Items);
        var old = Require(await OwnerVault.RevealVersionAsync(secret.Id, version.VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View }));
        Assert.Equal(markers.Value, old.Value);
    }

    [Fact]
    public async Task SharedEdit_OwnerCanRestorePreviousValue()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        var editor = SharingFor(TestUsers.ShareEditor);
        var opened = Require(await editor.GetSharedSecretAsync(OwnerId, secret.Id));
        var edited = Require(await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, opened.Item.ConcurrencyToken, EditBody(markers.Name, "recipient-value")));
        var version = Assert.Single(Require(await OwnerVault.ListHistoryAsync(secret.Id)).Items);

        Require(await OwnerVault.RestoreVersionAsync(secret.Id, version.VersionId, edited.ConcurrencyToken));

        Assert.Equal(markers.Value, await OwnerRevealValueAsync(secret.Id));
        Assert.Equal(markers.Value, await RevealValueAsync(editor, secret.Id));
    }

    [Fact]
    public async Task SharedEdit_ConcurrencyTokenFromRecipientWorksForOwnerAfterReload()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        var editor = SharingFor(TestUsers.ShareEditor);
        var opened = Require(await editor.GetSharedSecretAsync(OwnerId, secret.Id));
        var edited = Require(await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, opened.Item.ConcurrencyToken, EditBody("Recipient name")));

        var ownerView = Require(await OwnerVault.GetSecretAsync(secret.Id));
        Assert.Equal(edited.ConcurrencyToken, ownerView.ConcurrencyToken);
        var ownerEdit = await OwnerVault.UpdateSecretAsync(secret.Id, ownerView.ConcurrencyToken, EditBody("Owner name"));
        Assert.Equal(HttpStatusCode.OK, ownerEdit.Response.StatusCode);

        var stale = await editor.UpdateSharedSecretAsync(OwnerId, secret.Id, edited.ConcurrencyToken, EditBody("Late recipient name"));
        Assert.Equal(HttpStatusCode.Conflict, stale.Response.StatusCode);
    }

    [Fact]
    public async Task Grant_AfterEdit_RecipientReadsCurrentName()
    {
        var secret = await NewSecretAsync();
        Require(await OwnerVault.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, EditBody("Name after edit")));

        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var viewer = SharingFor(TestUsers.ShareViewer);
        Assert.Equal("Name after edit", Require(await viewer.GetSharedSecretAsync(OwnerId, secret.Id)).Item.Name);
        Assert.Equal("Name after edit", Assert.Single(await WholeSharedListAsync(viewer)).Name);
    }

    [Fact]
    public async Task RenameSecret_RecipientReadsNewName()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var viewer = SharingFor(TestUsers.ShareViewer);

        Require(await OwnerVault.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, EditBody("Renamed secret")));

        Assert.Equal("Renamed secret", Require(await viewer.GetSharedSecretAsync(OwnerId, secret.Id)).Item.Name);
        Assert.Equal("Renamed secret", Assert.Single(await WholeSharedListAsync(viewer)).Name);
    }

    [Fact]
    public async Task DirectAndFolderGrant_RevokeDirect_FolderStillGives()
    {
        var folder = await NewFolderAsync("Shared folder");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.Edit);
        var viewer = SharingFor(TestUsers.ShareViewer);

        Assert.Equal(SharePermissionDto.Edit, Require(await viewer.GetSharedSecretAsync(OwnerId, secret.Id)).Permission);
        Assert.Equal(HttpStatusCode.NoContent, (await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareViewer.ObjectId)).StatusCode);

        var after = Require(await viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        Assert.Equal(SharePermissionDto.View, after.Permission);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.UpdateSharedSecretAsync(OwnerId, secret.Id, after.Item.ConcurrencyToken, EditBody("x"))).Response.StatusCode);
    }

    [Fact]
    public async Task ManagerShares_NewRecipientCanOpen_OwnerSeesGrantedBy()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        var manager = SharingFor(TestUsers.ShareManager);

        var created = Require(await manager.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ManagerGrantee, SharePermissionDto.View)));

        Assert.Equal(TestUsers.ShareManager.DisplayName, created.GrantedByDisplayName);
        var grantee = SharingFor(TestUsers.ManagerGrantee);
        Assert.Equal(secret.Id, Require(await grantee.GetSharedSecretAsync(OwnerId, secret.Id)).Item.Id);
        var ownerList = Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id));
        var row = Assert.Single(ownerList.Items, item => item.RecipientId == TestUsers.ManagerGrantee.ObjectId);
        Assert.Equal(TestUsers.ShareManager.DisplayName, row.GrantedByDisplayName);
    }

    [Fact]
    public async Task RevokeManager_TheirGrantsRemainAndShowGrantedBy()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        Require(await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ManagerGrantee, SharePermissionDto.View)));

        Assert.Equal(HttpStatusCode.NoContent, (await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareManager.ObjectId)).StatusCode);

        var remaining = Assert.Single(Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id)).Items);
        Assert.Equal(TestUsers.ManagerGrantee.ObjectId, remaining.RecipientId);
        Assert.Equal(TestUsers.ShareManager.DisplayName, remaining.GrantedByDisplayName);
        Assert.True(remaining.CanChange);
    }

    [Fact]
    public async Task LockedRecipient_SharedRoutesRefusedAndUnlockRestoresWithoutGrantWrite()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var viewer = SharingFor(TestUsers.ShareViewer);
        Require(await viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        var rowsBefore = await VaultRowsSnapshotAsync();

        await LockUserAsync(TestUsers.ShareViewer.ObjectId);
        await AssertSecretStatusAsync(HttpStatusCode.Forbidden, viewer, secret.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.ListSharedItemsAsync()).Response.StatusCode);

        await UnlockUserAsync(TestUsers.ShareViewer.ObjectId);
        Require(await viewer.GetSharedSecretAsync(OwnerId, secret.Id));
        Assert.Equal(rowsBefore, await VaultRowsSnapshotAsync());
    }

    [Fact]
    public async Task LockedOwner_RecipientsKeepRevealingEditingAndSharing()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        var manager = SharingFor(TestUsers.ShareManager);

        await LockUserAsync(OwnerId);

        Assert.Equal(markers.Value, await RevealValueAsync(manager, secret.Id));
        var opened = Require(await manager.GetSharedSecretAsync(OwnerId, secret.Id));
        Require(await manager.UpdateSharedSecretAsync(OwnerId, secret.Id, opened.Item.ConcurrencyToken, EditBody("Edited while owner locked")));
        Require(await manager.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ManagerGrantee, SharePermissionDto.View)));
        Assert.Equal(HttpStatusCode.Forbidden, (await OwnerVault.GetSecretAsync(secret.Id)).Response.StatusCode);
    }

    [Fact]
    public async Task ShareToLockedUser_IsRejectedLikeAnUnknownUser()
    {
        var secret = await NewSecretAsync();
        await WarmUpAsync(TestUsers.LockedRecipient);
        await LockUserAsync(TestUsers.LockedRecipient.ObjectId);

        var locked = await OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.LockedRecipient, SharePermissionDto.View));
        var unknown = await OwnerSharing.CreateSecretGrantAsync(
            OwnerId,
            secret.Id,
            new CreateShareGrantRequestDto { RecipientId = Guid.NewGuid(), Permission = SharePermissionDto.View });

        Assert.Equal(HttpStatusCode.BadRequest, locked.Response.StatusCode);
        Assert.Equal(unknown.Response.StatusCode, locked.Response.StatusCode);
        Assert.Equal(await NormalisedAsync(unknown.Response), await NormalisedAsync(locked.Response));
    }

    [GeneratedRegex("\"traceId\":\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex TraceId();

    private static async Task<string> NormalisedAsync(HttpResponseMessage response) =>
        TraceId().Replace(await response.Content.ReadAsStringAsync(Token), string.Empty);

    private async Task<IReadOnlyList<string>> VaultRowsSnapshotAsync()
    {
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, OwnerId, Token);
        return [.. rows.Select(row => $"{row.RowKey}|{row.ETag}").Order(StringComparer.Ordinal)];
    }
}
