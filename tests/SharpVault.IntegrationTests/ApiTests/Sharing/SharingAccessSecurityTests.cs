// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingAccessSecurityTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    public static TheoryData<string> IdorCases() =>
    [
        "get-other-owner",
        "get-ungranted-item",
        "update-other-owner",
        "update-ungranted-item",
        "reveal-other-owner",
        "reveal-ungranted-item",
        "folder-ungranted",
        "filing-ungranted",
        "list-grants",
        "create-grant-other-owner",
        "change-grant-of-other-recipient",
        "revoke-grant-of-other-recipient",
    ];

    [Fact]
    public async Task SharedGet_OtherOwnersSecretNoGrant_Returns404IdenticalToRandomOwner()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await WarmUpAsync(TestUsers.OtherUser);

        var real = (await SharingFor(TestUsers.OtherUser).GetSharedSecretAsync(Owner.ObjectId, secret.Id)).Response;
        var random = (await SharingFor(TestUsers.OtherUser).GetSharedSecretAsync(Guid.NewGuid(), Guid.NewGuid())).Response;

        Assert.Equal(HttpStatusCode.NotFound, real.StatusCode);
        Assert.Equal(random.StatusCode, real.StatusCode);
        Assert.Equal(await NormalisedAsync(random), await NormalisedAsync(real));
    }

    [Fact]
    public async Task SharedReveal_NoGrant_Returns404AndWritesNoAudit()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await WarmUpAsync(TestUsers.OtherUser);
        var before = await SnapshotAsync();

        var status = await SharedRevealStatusAsync(TestUsers.OtherUser, secret.Id);

        Assert.Equal(HttpStatusCode.NotFound, status);
        var after = await SnapshotAsync();
        Assert.Equal(before.AuditRows, after.AuditRows);
        Assert.Equal(before.Rows, after.Rows);
    }

    [Fact]
    public async Task SharedGet_GrantToSomeoneElse_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.OtherUser, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareEditor, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.OtherUser, secret.Id));
    }

    [Theory]
    [MemberData(nameof(IdorCases))]
    public async Task SharingIdor_EveryRouteIdSwapped_Returns404AndChangesNothing(string route)
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        await WarmUpAsync(TestUsers.ShareViewer);
        var before = await SnapshotAsync();
        var api = SharingFor(TestUsers.ShareViewer);
        var stranger = TestUsers.OtherUser.ObjectId;

        var response = route switch
        {
            "get-other-owner" => (await api.GetSharedSecretAsync(stranger, scenario.SecretS)).Response,
            "get-ungranted-item" => (await api.GetSharedSecretAsync(scenario.OwnerId, scenario.SecretT)).Response,
            "update-other-owner" => (await api.UpdateSharedSecretAsync(stranger, scenario.SecretS, scenario.SecretToken, RenameBody("idor"))).Response,
            "update-ungranted-item" => (await api.UpdateSharedSecretAsync(scenario.OwnerId, scenario.SecretT, scenario.NestedToken, RenameBody("idor"))).Response,
            "reveal-other-owner" => (await api.RevealSharedSecretAsync(stranger, scenario.SecretS, ViewValue)).Response,
            "reveal-ungranted-item" => (await api.RevealSharedSecretAsync(scenario.OwnerId, scenario.SecretT, ViewValue)).Response,
            "folder-ungranted" => (await api.GetSharedFolderAsync(scenario.OwnerId, scenario.FolderF)).Response,
            "filing-ungranted" => await api.FileSharedSecretAsync(scenario.OwnerId, scenario.SecretT, new FileSharedItemRequestDto { FolderId = null }),
            "list-grants" => (await api.ListSecretGrantsAsync(scenario.OwnerId, scenario.SecretS)).Response,
            "create-grant-other-owner" => (await api.CreateSecretGrantAsync(stranger, scenario.SecretS, GrantBody(TestUsers.OtherUser, SharePermissionDto.Manage))).Response,
            "change-grant-of-other-recipient" => (await api.ChangeSecretGrantAsync(scenario.OwnerId, scenario.SecretS, TestUsers.ShareEditor.ObjectId, scenario.SecretToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response,
            "revoke-grant-of-other-recipient" => await api.RevokeSecretGrantAsync(scenario.OwnerId, scenario.SecretS, TestUsers.ShareEditor.ObjectId),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
        };

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Token);
        scenario.SecretMarkers.AssertAbsentFrom(text);
        scenario.NestedMarkers.AssertAbsentFrom(text);
        var after = await SnapshotAsync();
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(before.AuditRows, after.AuditRows);
    }

    [Fact]
    public async Task CreateGrant_OwnerRouteWithOtherOwnersSecret_Returns404()
    {
        var foreign = await NewSecretAsync(new PlaintextMarkers(), owner: TestUsers.OtherUser);
        var before = await SnapshotAsync();

        var response = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, foreign.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = await SnapshotAsync();
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, foreign.Id, TestUsers.OtherUser.ObjectId));
    }

    [Fact]
    public async Task CrossOwnerItemId_WithValidFolderGrant_Returns404()
    {
        var folder = await NewFolderAsync("granted");
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var foreignFolder = await NewFolderAsync("foreign", owner: TestUsers.OtherUser);
        var foreignSecret = await NewSecretAsync(new PlaintextMarkers(), foreignFolder.Id, TestUsers.OtherUser);

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, foreignSecret.Id, Owner.ObjectId));
        Assert.Equal(HttpStatusCode.NotFound, await SharedFolderStatusAsync(TestUsers.FolderViewer, foreignFolder.Id, Owner.ObjectId));
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, foreignSecret.Id, TestUsers.OtherUser.ObjectId));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.FolderViewer, foreignSecret.Id, Owner.ObjectId));
    }

    [Fact]
    public async Task SharedUpdate_Viewer_Returns404AndChangesNothing()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.ShareViewer).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("hijacked"))).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = await SnapshotAsync();
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(markers.Name, Require(await ApiFor(Owner).GetSecretAsync(secret.Id)).Name);
    }

    [Fact]
    public async Task SharedUpdate_FolderViewer_Returns404()
    {
        var folder = await NewFolderAsync("folder");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        var response = (await SharingFor(TestUsers.FolderViewer).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("hijacked"))).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("hijacked", Require(await ApiFor(Owner).GetSecretAsync(secret.Id)).Name);
    }

    [Fact]
    public async Task Revoke_ThenNextRequest_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.OK, await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await SharingFor(Owner).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id));
        var (_, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task ChangeToView_ThenUpdate_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        var edited = Require(await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("edited")));
        Require(await SharingFor(Owner).ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, grant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.View }));

        var response = (await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, edited.ConcurrencyToken, RenameBody("again"))).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareEditor, secret.Id));
        Assert.Equal("edited", Require(await ApiFor(Owner).GetSecretAsync(secret.Id)).Name);
    }

    [Fact]
    public async Task ShareAccessResolver_NoCaching_ReadsEveryCall()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await WarmUpAsync(TestUsers.ShareViewer);

        ResetCounters();
        await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id);
        var first = StorageRequests.VaultReads().Count;
        ResetCounters();
        await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id);
        var second = StorageRequests.VaultReads().Count;

        Assert.True(first > 0);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task MoveSecretOutOfSharedFolder_RecipientLosesAccess()
    {
        var folder = await NewFolderAsync("shared");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));

        Require(await ApiFor(Owner).MoveSecretAsync(secret.Id, secret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null }));

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.FolderViewer, secret.Id));
    }

    [Fact]
    public async Task MoveFolderOutOfSharedFolder_RecipientLosesNestedSecrets()
    {
        var parent = await NewFolderAsync("parent");
        var child = await NewFolderAsync("child", parent.Id);
        var secret = await NewSecretAsync(new PlaintextMarkers(), child.Id);
        await ShareFolderAsync(parent.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));

        Require(await ApiFor(Owner).MoveFolderAsync(child.Id, new MoveFolderRequestDto { ParentId = null }));

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedFolderStatusAsync(TestUsers.FolderViewer, child.Id));
    }

    [Fact]
    public async Task MoveSecretIntoSharedFolder_RecipientGainsAccess()
    {
        var folder = await NewFolderAsync("shared");
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));

        Require(await ApiFor(Owner).MoveSecretAsync(secret.Id, secret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folder.Id }));

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
    }

    [Fact]
    public async Task SecretAddedAfterFolderShare_RecipientSeesIt()
    {
        var folder = await NewFolderAsync("shared");
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        var contents = Require(await SharingFor(TestUsers.FolderViewer).GetSharedFolderAsync(Owner.ObjectId, folder.Id));
        Assert.Contains(contents.Items, item => item.Id == secret.Id);
    }

    [Fact]
    public async Task SecretInNestedFolder_RecipientSeesIt()
    {
        var top = await NewFolderAsync("level-1");
        var current = top;
        for (var level = 2; level <= 10; level++)
        {
            current = await NewFolderAsync($"level-{level}", current.Id);
        }

        var secret = await NewSecretAsync(new PlaintextMarkers(), current.Id);
        await ShareFolderAsync(top.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.OK, await SharedRevealStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.OK, await SharedFolderStatusAsync(TestUsers.FolderViewer, current.Id));
    }

    [Fact]
    public async Task DeleteFolder_RecipientLosesAccessAtOnce()
    {
        var folder = await NewFolderAsync("shared");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await ApiFor(Owner).DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedFolderStatusAsync(TestUsers.FolderViewer, folder.Id));
    }

    [Fact]
    public async Task DeletedParent_NestedSecretOfRestoredChild_GivesNoAccessFromParentGrant()
    {
        var parent = await NewFolderAsync("parent");
        var child = await NewFolderAsync("child", parent.Id);
        var secret = await NewSecretAsync(new PlaintextMarkers(), child.Id);
        await ShareFolderAsync(parent.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var owner = ApiFor(Owner);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFolderAsync(parent.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);

        var restored = await owner.RestoreRecycledFolderAsync(child.Id);

        Assert.True(restored.Response.IsSuccessStatusCode, $"Restoring the child returned {restored.Response.StatusCode}.");
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedFolderStatusAsync(TestUsers.FolderViewer, child.Id));
    }

    [Fact]
    public async Task FolderGrantOnRecycledFolder_InterimRestoredSecret_Returns404()
    {
        var folder = await NewFolderAsync("shared");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var owner = ApiFor(Owner);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);

        var restored = await owner.RestoreRecycledSecretAsync(secret.Id);

        Assert.True(restored.Response.IsSuccessStatusCode, $"Restoring the secret returned {restored.Response.StatusCode}.");
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.FolderViewer, secret.Id));
    }

    [Fact]
    public async Task DeleteSecret_RecipientGets404AtOnce()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await ApiFor(Owner).DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id));
        var (_, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Restore_RecipientRegainsDirectAccess()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var owner = ApiFor(Owner);
        await owner.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);

        Require(await owner.RestoreRecycledSecretAsync(secret.Id));

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.OK, await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id));
    }

    [Fact]
    public async Task RestoreToRoot_RecipientKeepsDirectLosesFolderAccess()
    {
        var folder = await NewFolderAsync("shared");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.View);
        var owner = ApiFor(Owner);
        await owner.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        Require(await owner.RestoreRecycledSecretAsync(secret.Id));

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareEditor, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
    }

    [Fact]
    public async Task PurgeSecret_RecipientGets404AndNotListed()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var owner = ApiFor(Owner);
        await owner.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PurgeRecycledSecretAsync(secret.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        var (_, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task InboxRowWithoutGrant_GivesNoAccessAndIsNotListed()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var inbox = Factory.Services.GetRequiredService<IShareInboxStore>();
        await inbox.AddIfAbsentAsync(
            new ShareInboxEntry
            {
                RecipientId = TestUsers.OtherUser.ObjectId,
                OwnerId = Owner.ObjectId,
                ItemKind = ShareItemKind.Secret,
                ItemId = secret.Id,
                SharedAt = DateTimeOffset.UtcNow,
            },
            Token);

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.OtherUser, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.OtherUser, secret.Id));
        var (response, list) = await SharingFor(TestUsers.OtherUser).ListSharedItemsAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Unlock_RestoresAccessWithNoGrantWrite()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await WarmUpAsync(TestUsers.ShareViewer);
        await LockUserAsync(TestUsers.ShareViewer.ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        var before = await SnapshotAsync();

        await SetUserStatusAsync(TestUsers.ShareViewer.ObjectId, UserRows.ActiveStatus);
        ResetCounters();

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Empty(StorageRequests.VaultWrites());
        var after = await SnapshotAsync();
        Assert.Equal(before.Rows, after.Rows);
    }

    [Fact]
    public async Task LockedRecipient_EverySharedRoute_Returns403()
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        var api = SharingFor(TestUsers.LockedRecipient);
        await WarmUpAsync(TestUsers.LockedRecipient);
        await LockUserAsync(TestUsers.LockedRecipient.ObjectId);

        var responses = new List<HttpResponseMessage>
        {
            (await api.ListSharedItemsAsync()).Response,
            (await api.GetSharedSecretAsync(scenario.OwnerId, scenario.SecretS)).Response,
            (await api.UpdateSharedSecretAsync(scenario.OwnerId, scenario.SecretS, scenario.SecretToken, RenameBody("locked"))).Response,
            (await api.RevealSharedSecretAsync(scenario.OwnerId, scenario.SecretS, ViewValue)).Response,
            (await api.GetSharedFolderAsync(scenario.OwnerId, scenario.FolderF)).Response,
            (await api.ListSecretGrantsAsync(scenario.OwnerId, scenario.SecretS)).Response,
            (await api.CreateSecretGrantAsync(scenario.OwnerId, scenario.SecretS, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response,
            await api.RevokeSecretGrantAsync(scenario.OwnerId, scenario.SecretS, TestUsers.ShareViewer.ObjectId),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }

    [Fact]
    public async Task LockedOwner_RecipientCanStillReveal()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await WarmUpAsync(Owner);
        await LockUserAsync(Owner.ObjectId);

        var (response, body) = await SharingFor(TestUsers.ShareViewer).RevealSharedSecretAsync(Owner.ObjectId, secret.Id, ViewValue);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(markers.Value, body!.Value);
    }

    [Fact]
    public async Task LockedOwner_ManagerCanStillShare()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        await WarmUpAsync(Owner);
        await LockUserAsync(Owner.ObjectId);

        var response = (await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task LockedOwner_OwnRoutesReturn403()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await WarmUpAsync(Owner);
        await LockUserAsync(Owner.ObjectId);
        var api = SharingFor(Owner);

        Assert.Equal(HttpStatusCode.Forbidden, (await api.ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApiFor(Owner).GetSecretAsync(secret.Id)).Response.StatusCode);
    }

    [Fact]
    public async Task Admin_NotOwnerNotRecipient_EverySharedRouteReturns404()
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        var api = SharingFor(TestUsers.Admin);

        var responses = new List<HttpResponseMessage>
        {
            (await api.GetSharedSecretAsync(scenario.OwnerId, scenario.SecretS)).Response,
            (await api.UpdateSharedSecretAsync(scenario.OwnerId, scenario.SecretS, scenario.SecretToken, RenameBody("admin"))).Response,
            (await api.RevealSharedSecretAsync(scenario.OwnerId, scenario.SecretS, ViewValue)).Response,
            (await api.GetSharedFolderAsync(scenario.OwnerId, scenario.FolderF)).Response,
            (await api.ListSecretGrantsAsync(scenario.OwnerId, scenario.SecretS)).Response,
            (await api.ListFolderGrantsAsync(scenario.OwnerId, scenario.FolderF)).Response,
            (await api.CreateSecretGrantAsync(scenario.OwnerId, scenario.SecretS, GrantBody(TestUsers.Admin, SharePermissionDto.Manage))).Response,
            (await api.CreateFolderGrantAsync(scenario.OwnerId, scenario.FolderF, GrantBody(TestUsers.Admin, SharePermissionDto.Manage))).Response,
            await api.RevokeSecretGrantAsync(scenario.OwnerId, scenario.SecretS, TestUsers.ShareViewer.ObjectId),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var (listResponse, list) = await api.ListSharedItemsAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Admin_AsRecipient_GetsOnlyThatPermission()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.Admin, SharePermissionDto.View);
        var api = SharingFor(TestUsers.Admin);

        Assert.Equal(HttpStatusCode.OK, (await api.GetSharedSecretAsync(Owner.ObjectId, secret.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.RevealSharedSecretAsync(Owner.ObjectId, secret.Id, ViewValue)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("admin"))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response.StatusCode);
    }
}
