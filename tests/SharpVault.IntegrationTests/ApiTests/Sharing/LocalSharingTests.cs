// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Authentication.Local;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalSharingTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.StorageRequests.Clear();
        factory.Logs.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    private VaultTestApi VaultFor(LocalAccount account) => new(factory.CreateClientForAccount(account));

    private SharingTestApi SharingFor(LocalAccount account) => new(factory.CreateClientForAccount(account));

    private async Task<(SecretDto Secret, PlaintextMarkers Markers)> OwnerCreatesSecretAsync(LocalAccount owner)
    {
        var markers = new PlaintextMarkers();
        var secret = Require(await VaultFor(owner).CreateSecretAsync(SharingScenario.NewSecret(markers, null)));
        return (secret, markers);
    }

    private async Task<ShareGrantDto> GrantAsync(LocalAccount owner, Guid secretId, LocalAccount recipient, SharePermissionDto permission) =>
        Require(await SharingFor(owner).CreateSecretGrantAsync(
            owner.Id,
            secretId,
            new CreateShareGrantRequestDto { RecipientId = recipient.Id, Permission = permission }));

    private static RevealSecretRequestDto Reveal(RevealFieldDto field) => new() { Field = field, Intent = RevealIntentDto.View };

    private async Task LockAsync(Guid id)
    {
        var table = factory.StorageAdminClient.GetTableClient(TableNames.Users);
        var row = await UserRows.GetAsync(table, id, Token);
        row![UserColumns.Status] = UserRows.LockedStatus;
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, Token);
    }

    [Fact]
    public async Task LocalUserOne_SharesWithUserTwo_UserTwoReveals()
    {
        var (secret, markers) = await OwnerCreatesSecretAsync(LocalAccounts.UserOne);
        await GrantAsync(LocalAccounts.UserOne, secret.Id, LocalAccounts.UserTwo, SharePermissionDto.View);
        var recipient = SharingFor(LocalAccounts.UserTwo);

        var listed = Require(await recipient.ListSharedItemsAsync());
        var detail = Require(await recipient.GetSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id));
        var value = Require(await recipient.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Value)));
        var notes = Require(await recipient.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Notes)));

        var item = Assert.Single(listed.Items);
        Assert.Equal(secret.Id, item.ItemId);
        Assert.Equal(LocalAccounts.UserOne.Id, item.OwnerId);
        Assert.Equal(SharePermissionDto.View, item.Permission);
        Assert.Equal(SharePermissionDto.View, detail.Permission);
        Assert.Equal(LocalAccounts.UserOne.DisplayName, detail.OwnerDisplayName);
        Assert.Equal(markers.Value, value.Value);
        Assert.Equal(markers.Notes, notes.Value);
        Assert.False(factory.Logs.ContainsText(markers.Value));
        Assert.False(factory.Logs.ContainsText(markers.Notes));
    }

    [Fact]
    public async Task LocalUserOne_RevokesFromUserTwo_UserTwoGets404()
    {
        var (secret, markers) = await OwnerCreatesSecretAsync(LocalAccounts.UserOne);
        await GrantAsync(LocalAccounts.UserOne, secret.Id, LocalAccounts.UserTwo, SharePermissionDto.Manage);
        var recipient = SharingFor(LocalAccounts.UserTwo);
        Require(await recipient.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Value)));
        var current = Require(await VaultFor(LocalAccounts.UserOne).GetSecretAsync(secret.Id));

        var revoked = await SharingFor(LocalAccounts.UserOne).RevokeSecretGrantAsync(LocalAccounts.UserOne.Id, secret.Id, LocalAccounts.UserTwo.Id);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Value))).Response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await recipient.UpdateSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, current.ConcurrencyToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "after-revoke" })).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.ListSecretGrantsAsync(LocalAccounts.UserOne.Id, secret.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.FileSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, new FileSharedItemRequestDto())).StatusCode);
        Assert.Empty(Require(await recipient.ListSharedItemsAsync()).Items);
        Assert.Equal(markers.Name, Require(await VaultFor(LocalAccounts.UserOne).GetSecretAsync(secret.Id)).Name);
    }

    [Fact]
    public async Task LocalUserOne_RevokesFolderFromUserTwo_NestedSecretReturns404()
    {
        var owner = VaultFor(LocalAccounts.UserOne);
        var markers = new PlaintextMarkers();
        var folder = Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = Require(await owner.CreateSecretAsync(SharingScenario.NewSecret(markers, folder.Id)));
        Require(await SharingFor(LocalAccounts.UserOne).CreateFolderGrantAsync(
            LocalAccounts.UserOne.Id,
            folder.Id,
            new CreateShareGrantRequestDto { RecipientId = LocalAccounts.UserTwo.Id, Permission = SharePermissionDto.View }));
        var recipient = SharingFor(LocalAccounts.UserTwo);
        Assert.Equal(markers.Value, Require(await recipient.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Value))).Value);

        var revoked = await SharingFor(LocalAccounts.UserOne).RevokeFolderGrantAsync(LocalAccounts.UserOne.Id, folder.Id, LocalAccounts.UserTwo.Id);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetSharedFolderAsync(LocalAccounts.UserOne.Id, folder.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Value))).Response.StatusCode);
    }

    [Fact]
    public async Task LocalAdminOne_NoShare_Returns404()
    {
        var (secret, markers) = await OwnerCreatesSecretAsync(LocalAccounts.UserOne);
        var folder = Require(await VaultFor(LocalAccounts.UserOne).CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var admin = SharingFor(LocalAccounts.AdminOne);

        var responses = new[]
        {
            (await admin.GetSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id)).Response,
            (await admin.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Value))).Response,
            (await admin.GetSharedFolderAsync(LocalAccounts.UserOne.Id, folder.Id)).Response,
            (await admin.ListSecretGrantsAsync(LocalAccounts.UserOne.Id, secret.Id)).Response,
            (await admin.CreateSecretGrantAsync(LocalAccounts.UserOne.Id, secret.Id, new CreateShareGrantRequestDto { RecipientId = LocalAccounts.AdminOne.Id, Permission = SharePermissionDto.Manage })).Response,
            (await VaultFor(LocalAccounts.AdminOne).GetSecretAsync(secret.Id)).Response,
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync(Token);
            markers.AssertAbsentFrom(body);
        }

        Assert.Empty(Require(await admin.ListSharedItemsAsync()).Items);
    }

    [Fact]
    public async Task LocalAdminOne_AsRecipient_GetsPermissionOnly()
    {
        var (shared, markers) = await OwnerCreatesSecretAsync(LocalAccounts.UserOne);
        var (other, otherMarkers) = await OwnerCreatesSecretAsync(LocalAccounts.UserOne);
        var grant = await GrantAsync(LocalAccounts.UserOne, shared.Id, LocalAccounts.AdminOne, SharePermissionDto.View);
        var admin = SharingFor(LocalAccounts.AdminOne);
        var current = Require(await VaultFor(LocalAccounts.UserOne).GetSecretAsync(shared.Id));
        var update = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "admin-edit" };

        Assert.Equal(markers.Value, Require(await admin.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, shared.Id, Reveal(RevealFieldDto.Value))).Value);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.UpdateSharedSecretAsync(LocalAccounts.UserOne.Id, shared.Id, current.ConcurrencyToken, update)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.ListSecretGrantsAsync(LocalAccounts.UserOne.Id, shared.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetSharedSecretAsync(LocalAccounts.UserOne.Id, other.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, other.Id, Reveal(RevealFieldDto.Value))).Response.StatusCode);

        Require(await SharingFor(LocalAccounts.UserOne).ChangeSecretGrantAsync(
            LocalAccounts.UserOne.Id,
            shared.Id,
            LocalAccounts.AdminOne.Id,
            grant.ConcurrencyToken,
            new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit }));

        Assert.Equal("admin-edit", Require(await admin.UpdateSharedSecretAsync(LocalAccounts.UserOne.Id, shared.Id, current.ConcurrencyToken, update)).Name);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.ListSecretGrantsAsync(LocalAccounts.UserOne.Id, shared.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetSharedSecretAsync(LocalAccounts.UserOne.Id, other.Id)).Response.StatusCode);
        var body = await (await admin.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, other.Id, Reveal(RevealFieldDto.Value))).Response.Content.ReadAsStringAsync(Token);
        otherMarkers.AssertAbsentFrom(body);
    }

    [Fact]
    public async Task LocalSearch_FindsTheFourAccountsOnly()
    {
        foreach (var caller in LocalAccounts.All)
        {
            var found = Require(await SharingFor(caller).SearchDirectoryAsync("sharpvault.invalid"));

            Assert.Equal(
                LocalAccounts.All.Where(account => account.Id != caller.Id).Select(account => account.Id).Order(),
                found.Items.Select(item => item.Id).Order());
            Assert.All(found.Items, item => Assert.Equal(LocalAccounts.All.Single(account => account.Id == item.Id).DisplayName, item.DisplayName));
        }

        var admins = Require(await SharingFor(LocalAccounts.UserOne).SearchDirectoryAsync("admin"));
        Assert.Equal([LocalAccounts.AdminOne.Id, LocalAccounts.AdminTwo.Id], admins.Items.Select(item => item.Id).Order());
        Assert.Empty(Require(await SharingFor(LocalAccounts.UserOne).SearchDirectoryAsync("nobody-here")).Items);
    }

    [Fact]
    public async Task LocalSearch_LockedAccount_NotReturned()
    {
        await LockAsync(LocalAccounts.UserTwo.Id);

        var found = Require(await SharingFor(LocalAccounts.UserOne).SearchDirectoryAsync("sharpvault.invalid"));

        Assert.Equal([LocalAccounts.AdminOne.Id, LocalAccounts.AdminTwo.Id], found.Items.Select(item => item.Id).Order());
        var byName = Require(await SharingFor(LocalAccounts.UserOne).SearchDirectoryAsync("User Two"));
        Assert.Empty(byName.Items);
    }

    [Fact]
    public async Task LocalSearch_ShortOrIllegalTerm_IsRejectedAndAnODataTermMatchesNobody()
    {
        var caller = SharingFor(LocalAccounts.UserOne);

        var tooShort = (await caller.SearchDirectoryAsync("ab")).Response;
        var illegal = (await caller.SearchDirectoryAsync("<script>alert(1)</script>")).Response;
        var injection = Require(await caller.SearchDirectoryAsync("' or PartitionKey ne '"));

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, illegal.StatusCode);
        Assert.Empty(injection.Items);
    }

    [Theory]
    [InlineData("Anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("LockedRecipient", HttpStatusCode.Forbidden)]
    public async Task LocalDeniedCallers_SharedRoutes_ReturnExpected(string caller, HttpStatusCode expected)
    {
        var (secret, markers) = await OwnerCreatesSecretAsync(LocalAccounts.UserOne);
        await GrantAsync(LocalAccounts.UserOne, secret.Id, LocalAccounts.UserTwo, SharePermissionDto.Manage);
        await LockAsync(LocalAccounts.UserTwo.Id);
        using var client = caller == "Anonymous" ? factory.CreateUnauthenticatedClient() : factory.CreateClientForAccount(LocalAccounts.UserTwo);
        var api = new SharingTestApi(client);

        var responses = new[]
        {
            (await api.ListSharedItemsAsync()).Response,
            (await api.GetSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id)).Response,
            (await api.RevealSharedSecretAsync(LocalAccounts.UserOne.Id, secret.Id, Reveal(RevealFieldDto.Value))).Response,
            (await api.ListSecretGrantsAsync(LocalAccounts.UserOne.Id, secret.Id)).Response,
            (await api.SearchDirectoryAsync("sharpvault")).Response,
        };

        Assert.All(responses, response => Assert.Equal(expected, response.StatusCode));
        foreach (var response in responses)
        {
            markers.AssertAbsentFrom(await response.Content.ReadAsStringAsync(Token));
        }
    }
}
