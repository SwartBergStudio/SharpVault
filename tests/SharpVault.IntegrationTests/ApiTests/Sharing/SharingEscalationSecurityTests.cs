// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingEscalationSecurityTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    public static TheoryData<string> ServerControlledMembers() => ["ownerId", "grantedById", "id", "grantedBy", "recipientDisplayName"];

    public static TheoryData<string> InvalidPermissions() =>
    [
        "\"Owner\"",
        "4",
        "3",
        "\"view\"",
        "\"Manage,Edit\"",
        "\"\"",
        "null",
        "0",
        "\"None\"",
    ];

    private async Task<(Guid SecretId, ShareGrantDto ManagerGrant)> ManagedSecretAsync()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var managerGrant = await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        return (secret.Id, managerGrant);
    }

    [Fact]
    public async Task CreateGrant_ManagerToSelf_Returns400()
    {
        var (secretId, _) = await ManagedSecretAsync();
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(Owner.ObjectId, secretId, GrantBody(TestUsers.ShareManager, SharePermissionDto.Manage))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task CreateGrant_ManagerToOwner_Returns400()
    {
        var (secretId, _) = await ManagedSecretAsync();
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(Owner.ObjectId, secretId, GrantBody(Owner, SharePermissionDto.Manage))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task CreateGrant_OwnerToSelf_Returns400()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());

        var response = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(Owner, SharePermissionDto.Manage))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeGrant_ManagerOwnGrant_Returns404()
    {
        var (secretId, managerGrant) = await ManagedSecretAsync();
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.ShareManager).ChangeSecretGrantAsync(
            Owner.ObjectId,
            secretId,
            TestUsers.ShareManager.ObjectId,
            managerGrant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.View })).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task ChangeGrant_ManagerOnOwnersGrant_Returns404AndChangesNothing()
    {
        var (secretId, _) = await ManagedSecretAsync();
        var viewerGrant = await ShareSecretAsync(secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.ShareManager).ChangeSecretGrantAsync(
            Owner.ObjectId,
            secretId,
            TestUsers.ShareViewer.ObjectId,
            viewerGrant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
        Assert.Equal(before.AuditRows, (await SnapshotAsync()).AuditRows);
    }

    [Fact]
    public async Task CreateGrant_ManagerGrantingManage_Returns400AndNoRows()
    {
        var (secretId, _) = await ManagedSecretAsync();
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(Owner.ObjectId, secretId, GrantBody(TestUsers.ShareViewer, SharePermissionDto.Manage))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task ChangeGrant_ManagerRaisingTheirGrantToManage_Returns400AndNoChange()
    {
        var (secretId, _) = await ManagedSecretAsync();
        var editorGrant = await ShareSecretAsync(secretId, TestUsers.ShareViewer, SharePermissionDto.Edit, by: TestUsers.ShareManager);
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.ShareManager).ChangeSecretGrantAsync(
            Owner.ObjectId,
            secretId,
            TestUsers.ShareViewer.ObjectId,
            editorGrant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task CreateGrant_ManagerGrantingEdit_Returns201()
    {
        var (secretId, _) = await ManagedSecretAsync();

        var response = (await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(Owner.ObjectId, secretId, GrantBody(TestUsers.ShareViewer, SharePermissionDto.Edit))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateGrant_OwnerGrantingManage_Returns201()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());

        var response = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.Manage))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RevokeGrant_ManagerOnOwnersGrant_Returns404()
    {
        var (secretId, _) = await ManagedSecretAsync();
        await ShareSecretAsync(secretId, TestUsers.ShareViewer, SharePermissionDto.View);

        var response = await SharingFor(TestUsers.ShareManager).RevokeSecretGrantAsync(Owner.ObjectId, secretId, TestUsers.ShareViewer.ObjectId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secretId));
    }

    [Fact]
    public async Task RevokeGrant_ManagerOnOtherManagersGrant_Returns404()
    {
        var (secretId, _) = await ManagedSecretAsync();
        await ShareSecretAsync(secretId, TestUsers.FolderManager, SharePermissionDto.Manage);
        await ShareSecretAsync(secretId, TestUsers.ManagerGrantee, SharePermissionDto.View, by: TestUsers.FolderManager);

        var response = await SharingFor(TestUsers.ShareManager).RevokeSecretGrantAsync(Owner.ObjectId, secretId, TestUsers.ManagerGrantee.ObjectId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ManagerGrantee, secretId));
    }

    [Fact]
    public async Task ChangeGrant_ManagerOnOtherManagersGrant_Returns404()
    {
        var (secretId, _) = await ManagedSecretAsync();
        await ShareSecretAsync(secretId, TestUsers.FolderManager, SharePermissionDto.Manage);
        var granteeGrant = await ShareSecretAsync(secretId, TestUsers.ManagerGrantee, SharePermissionDto.View, by: TestUsers.FolderManager);

        var response = (await SharingFor(TestUsers.ShareManager).ChangeSecretGrantAsync(
            Owner.ObjectId,
            secretId,
            TestUsers.ManagerGrantee.ObjectId,
            granteeGrant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateGrant_Editor_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);

        var response = (await SharingFor(TestUsers.ShareEditor).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.OtherUser, secret.Id));
    }

    [Fact]
    public async Task ListGrants_Viewer_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);

        var response = (await SharingFor(TestUsers.ShareViewer).ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(TestUsers.ShareEditor.DisplayName, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListGrants_Editor_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var response = (await SharingFor(TestUsers.ShareEditor).ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(TestUsers.ShareViewer.DisplayName, text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ServerControlledMembers))]
    public async Task CreateGrant_UnknownMember_Returns400(string member)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var path = VaultTestApi.Bind(SharpVault.Dtos.ApiRoutes.SecretGrants, ("ownerId", Owner.ObjectId), ("secretId", secret.Id));
        var json = $"{{\"recipientId\":\"{TestUsers.ShareViewer.ObjectId:D}\",\"permission\":\"View\",\"{member}\":\"{TestUsers.OtherUser.ObjectId:D}\"}}";
        var before = await SnapshotAsync();

        var response = await ApiFor(Owner).PostRawAsync(path, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Theory]
    [MemberData(nameof(InvalidPermissions))]
    public async Task CreateGrant_PermissionOwnerOrNumericOrLowercase_Returns400(string permissionJson)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var path = VaultTestApi.Bind(SharpVault.Dtos.ApiRoutes.SecretGrants, ("ownerId", Owner.ObjectId), ("secretId", secret.Id));
        var json = $"{{\"recipientId\":\"{TestUsers.ShareViewer.ObjectId:D}\",\"permission\":{permissionJson}}}";
        var before = await SnapshotAsync();

        var response = await ApiFor(Owner).PostRawAsync(path, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Theory]
    [MemberData(nameof(InvalidPermissions))]
    public async Task ChangeGrant_PermissionOwnerOrNumericOrLowercase_Returns400(string permissionJson)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var path = VaultTestApi.Bind(SharpVault.Dtos.ApiRoutes.SecretGrant, ("ownerId", Owner.ObjectId), ("secretId", secret.Id), ("recipientId", TestUsers.ShareViewer.ObjectId));

        var response = await ApiFor(Owner).PutRawAsync(path, $"{{\"permission\":{permissionJson}}}", grant.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await SharingFor(TestUsers.ShareViewer).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("view-only"))).Response.StatusCode);
    }

    [Fact]
    public async Task EffectivePermission_DirectViewFolderEdit_IsEdit()
    {
        var folder = await NewFolderAsync("folder");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.View);
        await ShareFolderAsync(folder.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);

        var (response, body) = await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("edited-via-folder"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("edited-via-folder", body!.Name);
        var shared = Require(await SharingFor(TestUsers.ShareEditor).GetSharedSecretAsync(Owner.ObjectId, secret.Id));
        Assert.Equal(SharePermissionDto.Edit, shared.Permission);
    }

    [Fact]
    public async Task EffectivePermission_DirectManageFolderView_IsManage()
    {
        var folder = await NewFolderAsync("folder");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareFolderAsync(folder.Id, TestUsers.ShareManager, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);

        var response = (await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateFolderGrant_InheritedManagerGrantingManage_Returns400()
    {
        var parent = await NewFolderAsync("parent");
        var child = await NewFolderAsync("child", parent.Id);
        await ShareFolderAsync(child.Id, TestUsers.FolderManager, SharePermissionDto.View);
        await ShareFolderAsync(parent.Id, TestUsers.FolderManager, SharePermissionDto.Manage);
        var before = await SnapshotAsync();

        var response = (await SharingFor(TestUsers.FolderManager).CreateFolderGrantAsync(Owner.ObjectId, child.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.Manage))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task EffectivePermission_ChildViewParentManage_IsManage()
    {
        var parent = await NewFolderAsync("parent");
        var child = await NewFolderAsync("child", parent.Id);
        var secret = await NewSecretAsync(new PlaintextMarkers(), child.Id);
        await ShareFolderAsync(child.Id, TestUsers.FolderManager, SharePermissionDto.View);
        await ShareFolderAsync(parent.Id, TestUsers.FolderManager, SharePermissionDto.Manage);

        var response = (await SharingFor(TestUsers.FolderManager).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task EffectivePermission_FolderViewOnlyAcrossChain_CannotShare()
    {
        var parent = await NewFolderAsync("parent");
        var child = await NewFolderAsync("child", parent.Id);
        var secret = await NewSecretAsync(new PlaintextMarkers(), child.Id);
        await ShareFolderAsync(parent.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        await ShareFolderAsync(child.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        var response = (await SharingFor(TestUsers.FolderViewer).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.OtherUser, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokeManager_TheirGrantsRemainAndShowGrantedBy()
    {
        var (secretId, _) = await ManagedSecretAsync();
        await ShareSecretAsync(secretId, TestUsers.ManagerGrantee, SharePermissionDto.View, by: TestUsers.ShareManager);

        Assert.Equal(HttpStatusCode.NoContent, (await SharingFor(Owner).RevokeSecretGrantAsync(Owner.ObjectId, secretId, TestUsers.ShareManager.ObjectId)).StatusCode);

        var list = Require(await SharingFor(Owner).ListSecretGrantsAsync(Owner.ObjectId, secretId));
        var remaining = Assert.Single(list.Items);
        Assert.Equal(TestUsers.ManagerGrantee.ObjectId, remaining.RecipientId);
        Assert.Equal(TestUsers.ShareManager.DisplayName, remaining.GrantedByDisplayName);
        Assert.True(remaining.CanChange);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ManagerGrantee, secretId));
    }

    [Fact]
    public async Task ShareAuditEntries_HoldIdsOnlyNeverNames()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        Require(await SharingFor(Owner).ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, grant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.View }));
        await SharingFor(Owner).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId);

        var rows = await VaultAuditRowsAsync(AuditEventType.SecretShared, AuditEventType.SecretShareChanged, AuditEventType.SecretShareRevoked);
        var secretRows = await SecretAuditRowsAsync(Owner.ObjectId, secret.Id);

        Assert.Equal(3, rows.Count);
        var texts = rows.Concat(secretRows).Select(row => string.Join('|', row.Select(pair => pair.Value?.ToString()))).ToList();
        foreach (var text in texts)
        {
            Assert.DoesNotContain(TestUsers.ShareEditor.DisplayName, text, StringComparison.Ordinal);
            Assert.DoesNotContain(TestUsers.ShareEditor.Email!, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Owner.DisplayName, text, StringComparison.Ordinal);
            markers.AssertAbsentFrom(text);
        }

        Assert.Contains(texts, text => text.Contains(TestUsers.ShareEditor.ObjectId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }
}
