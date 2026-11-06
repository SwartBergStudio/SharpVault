// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Local;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalSharingClientsTests : IAsyncLifetime
{
    private const string SecretValue = "local-share-client-value-7c3d";

    private readonly LocalApiTestFactory _api;
    private readonly LocalWebTestFactory _web;

    public LocalSharingClientsTests(LocalApiTestFactory api, LocalWebTestFactory web)
    {
        _api = api;
        _web = web;
        _web.UseApi(api);
    }

    public async ValueTask InitializeAsync()
    {
        await _api.ResetAndSeedAsync(CancellationToken.None);
        _api.StorageRequests.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private WebCircuit CircuitFor(LocalAccount account) => WebCircuit.For(_web.Services, LocalPrincipalFactory.Create(account, "test"));

    private static T Succeeded<T>(ApiResponse<T> response)
    {
        Assert.Equal(ApiCallStatus.Success, response.Status);
        return response.Value!;
    }

    [Fact]
    public async Task LocalClients_ShareFileRevealUpdateAndRevoke_WorkEndToEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = CircuitFor(LocalAccounts.UserOne);
        using var recipient = CircuitFor(LocalAccounts.UserTwo);
        var secret = Succeeded(await owner.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Shared login", Value = SecretValue },
            cancellationToken));
        var found = Succeeded(await owner.Directory.SearchUsersAsync("user.two", cancellationToken));
        var target = Assert.Single(found.Items);
        Assert.Equal(LocalAccounts.UserTwo.Id, target.Id);

        var grant = Succeeded(await owner.ShareGrants.CreateSecretGrantAsync(
            LocalAccounts.UserOne.Id,
            secret.Id,
            new CreateShareGrantRequestDto { RecipientId = target.Id, Permission = SharePermissionDto.Edit },
            cancellationToken));

        var listed = Succeeded(await recipient.SharedItems.ListAsync(null, 50, null, cancellationToken));
        var item = Assert.Single(listed.Items);
        Assert.Equal(secret.Id, item.ItemId);
        Assert.Equal(SharePermissionDto.Edit, item.Permission);
        var revealed = Succeeded(await recipient.SharedItems.RevealSecretAsync(
            LocalAccounts.UserOne.Id,
            secret.Id,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.Copy },
            cancellationToken));
        Assert.Equal(SecretValue, revealed.Value);
        var detail = Succeeded(await recipient.SharedItems.GetSecretAsync(LocalAccounts.UserOne.Id, secret.Id, cancellationToken));
        var updated = Succeeded(await recipient.SharedItems.UpdateSecretAsync(
            LocalAccounts.UserOne.Id,
            secret.Id,
            detail.Item.ConcurrencyToken,
            new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Renamed by recipient" },
            cancellationToken));
        Assert.Equal("Renamed by recipient", updated.Name);
        Assert.Equal(ApiCallStatus.Success, (await recipient.SharedItems.FileSecretAsync(LocalAccounts.UserOne.Id, secret.Id, new FileSharedItemRequestDto(), cancellationToken)).Status);

        var revoked = await owner.ShareGrants.RevokeSecretGrantAsync(LocalAccounts.UserOne.Id, secret.Id, target.Id, cancellationToken);

        Assert.Equal(ApiCallStatus.Success, revoked.Status);
        Assert.Equal(ApiCallStatus.NotFound, (await recipient.SharedItems.GetSecretAsync(LocalAccounts.UserOne.Id, secret.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await recipient.SharedItems.RevealSecretAsync(LocalAccounts.UserOne.Id, secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken)).Status);
        Assert.Empty(Succeeded(await recipient.SharedItems.ListAsync(null, 50, null, cancellationToken)).Items);
        Assert.False(string.IsNullOrEmpty(grant.ConcurrencyToken));
        Assert.Empty(_api.StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task LocalClients_AdminOneWithoutAGrant_GetsNotFoundOnEverySharedCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = CircuitFor(LocalAccounts.UserOne);
        using var admin = CircuitFor(LocalAccounts.AdminOne);
        var secret = Succeeded(await owner.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Owned", Value = SecretValue },
            cancellationToken));
        var folder = Succeeded(await owner.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Owned folder" }, cancellationToken));

        Assert.Equal(ApiCallStatus.NotFound, (await admin.SharedItems.GetSecretAsync(LocalAccounts.UserOne.Id, secret.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await admin.SharedItems.RevealSecretAsync(LocalAccounts.UserOne.Id, secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await admin.SharedItems.GetFolderAsync(LocalAccounts.UserOne.Id, folder.Id, 50, null, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await admin.ShareGrants.ListSecretGrantsAsync(LocalAccounts.UserOne.Id, secret.Id, cancellationToken)).Status);
        Assert.Empty(Succeeded(await admin.SharedItems.ListAsync(null, 50, null, cancellationToken)).Items);
    }
}
