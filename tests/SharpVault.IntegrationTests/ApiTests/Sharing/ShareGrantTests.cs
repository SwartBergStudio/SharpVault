// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Dtos.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
public sealed class ShareGrantTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private const int RacingCreators = 5;

    private PurgeGrantWorld World
    {
        get
        {
            _ = Factory.Server;
            return new PurgeGrantWorld(
                Factory.Services.GetRequiredService<IVaultStore>(),
                Factory.Services.GetRequiredService<IShareInboxStore>(),
                GetTableClient(TableNames.Vaults),
                GetTableClient(TableNames.ShareInbox),
                OwnerId);
        }
    }

    private static readonly TestUser[] RacingRecipients =
    [
        TestUsers.ShareViewer,
        TestUsers.ShareEditor,
        TestUsers.ShareManager,
        TestUsers.ManagerGrantee,
        TestUsers.FolderViewer,
    ];

    [Fact]
    public async Task CreateSecretGrant_Valid_Returns201WithGrantDetails()
    {
        var secret = await NewSecretAsync();

        var result = await OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ShareEditor, SharePermissionDto.Edit));

        Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
        var grant = Assert.IsType<ShareGrantDto>(result.Body);
        Assert.Equal(TestUsers.ShareEditor.ObjectId, grant.RecipientId);
        Assert.Equal(TestUsers.ShareEditor.DisplayName, grant.RecipientDisplayName);
        Assert.Equal(SharePermissionDto.Edit, grant.Permission);
        Assert.Equal(TestUsers.User.DisplayName, grant.GrantedByDisplayName);
        Assert.True(grant.CanChange);
        Assert.False(string.IsNullOrWhiteSpace(grant.ConcurrencyToken));
    }

    [Fact]
    public async Task ListSecretGrants_AfterGrants_ReturnsEveryRecipient()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);

        var list = Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id));

        Assert.Equal(2, list.Items.Count);
        Assert.Contains(list.Items, item => item.RecipientId == TestUsers.ShareViewer.ObjectId && item.Permission == SharePermissionDto.View);
        Assert.Contains(list.Items, item => item.RecipientId == TestUsers.ShareEditor.ObjectId && item.Permission == SharePermissionDto.Edit);
    }

    [Fact]
    public async Task CreateSecretGrant_SameRecipientTwice_ReturnsConflict()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var second = await OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.Edit));

        Assert.Equal(HttpStatusCode.Conflict, second.Response.StatusCode);
        Assert.Equal(SharePermissionDto.View, Assert.Single(Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id)).Items).Permission);
    }

    [Fact]
    public async Task CreateSecretGrant_RecipientIsOwner_Returns400()
    {
        var secret = await NewSecretAsync();

        var result = await OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(Owner, SharePermissionDto.View));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
    }

    [Fact]
    public async Task CreateSecretGrant_ManagerToOwner_Returns400()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);

        var result = await SharingFor(TestUsers.ShareManager).CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(Owner, SharePermissionDto.View));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
    }

    [Fact]
    public async Task ChangeSecretGrant_ValidToken_UpdatesPermissionAndToken()
    {
        var secret = await NewSecretAsync();
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var changed = Require(await OwnerSharing.ChangeSecretGrantAsync(
            OwnerId,
            secret.Id,
            TestUsers.ShareViewer.ObjectId,
            grant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage }));

        Assert.Equal(SharePermissionDto.Manage, changed.Permission);
        Assert.NotEqual(grant.ConcurrencyToken, changed.ConcurrencyToken);
        Assert.Equal(SharePermissionDto.Manage, Require(await SharingFor(TestUsers.ShareViewer).GetSharedSecretAsync(OwnerId, secret.Id)).Permission);
    }

    [Fact]
    public async Task ChangeSecretGrant_StaleToken_Returns409AndChangesNothing()
    {
        var secret = await NewSecretAsync();
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Require(await OwnerSharing.ChangeSecretGrantAsync(
            OwnerId,
            secret.Id,
            TestUsers.ShareViewer.ObjectId,
            grant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit }));

        var stale = await OwnerSharing.ChangeSecretGrantAsync(
            OwnerId,
            secret.Id,
            TestUsers.ShareViewer.ObjectId,
            grant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage });

        Assert.Equal(HttpStatusCode.Conflict, stale.Response.StatusCode);
        Assert.Equal(SharePermissionDto.Edit, Assert.Single(Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id)).Items).Permission);
    }

    [Fact]
    public async Task RevokeSecretGrant_ThenRepeat_Returns204Then404()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var first = await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareViewer.ObjectId);
        var second = await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareViewer.ObjectId);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Empty(Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id)).Items);
    }

    [Fact]
    public async Task FolderGrant_CreateListChangeRevoke_Works()
    {
        var folder = await NewFolderAsync("Grant folder");

        var created = await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var listed = Assert.Single(Require(await OwnerSharing.ListFolderGrantsAsync(OwnerId, folder.Id)).Items);
        Assert.Equal(TestUsers.FolderViewer.ObjectId, listed.RecipientId);

        var changed = Require(await OwnerSharing.ChangeFolderGrantAsync(
            OwnerId,
            folder.Id,
            TestUsers.FolderViewer.ObjectId,
            created.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit }));
        Assert.Equal(SharePermissionDto.Edit, changed.Permission);

        Assert.Equal(HttpStatusCode.NoContent, (await OwnerSharing.RevokeFolderGrantAsync(OwnerId, folder.Id, TestUsers.FolderViewer.ObjectId)).StatusCode);
        Assert.Empty(Require(await OwnerSharing.ListFolderGrantsAsync(OwnerId, folder.Id)).Items);
        await AssertFolderStatusAsync(HttpStatusCode.NotFound, SharingFor(TestUsers.FolderViewer), folder.Id);
    }

    [Fact]
    public async Task CreateSecretGrant_At99_Succeeds()
    {
        var secret = await NewSecretAsync();
        await World.SeedGrantsAsync(secret.Id, SharingLimits.MaxGrantsPerItem - 1, Token);

        var result = await OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View));

        Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
    }

    [Fact]
    public async Task CreateSecretGrant_At100_Returns400AndAddsNothing()
    {
        var secret = await NewSecretAsync();
        await World.SeedGrantsAsync(secret.Id, SharingLimits.MaxGrantsPerItem, Token);

        var result = await OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        await AssertSecretStatusAsync(HttpStatusCode.NotFound, SharingFor(TestUsers.ShareViewer), secret.Id);
    }

    [Fact]
    public async Task ParallelCreateSameRecipient_OneSucceedsOthersConflict()
    {
        var secret = await NewSecretAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, RacingCreators).Select(_ =>
            OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))));

        Assert.Equal(1, results.Count(result => result.Response.StatusCode == HttpStatusCode.Created));
        Assert.All(results, result => Assert.True(result.Response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict));
        Assert.Single(Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id)).Items);
    }

    [Fact]
    public async Task ParallelCreatesAtOneBelowCap_OvershootIsBoundedAndEveryGrantStaysListed()
    {
        var secret = await NewSecretAsync();
        var seeded = SharingLimits.MaxGrantsPerItem - 1;
        await World.SeedGrantsAsync(secret.Id, seeded, Token);

        var results = await Task.WhenAll(RacingRecipients.Select(recipient =>
            OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(recipient, SharePermissionDto.View))));

        var created = results.Count(result => result.Response.StatusCode == HttpStatusCode.Created);
        var listed = Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id)).Items;
        Assert.Equal(seeded + created, listed.Count);
        Assert.True(listed.Count <= seeded + RacingCreators);
        var later = await OwnerSharing.CreateSecretGrantAsync(OwnerId, secret.Id, GrantBody(TestUsers.FolderManager, SharePermissionDto.View));
        Assert.Equal(HttpStatusCode.BadRequest, later.Response.StatusCode);
        foreach (var grant in listed)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, grant.RecipientId)).StatusCode);
        }

        Assert.Empty(Require(await OwnerSharing.ListSecretGrantsAsync(OwnerId, secret.Id)).Items);
    }
}
