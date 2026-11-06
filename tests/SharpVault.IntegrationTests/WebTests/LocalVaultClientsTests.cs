// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Local;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalVaultClientsTests : IAsyncLifetime
{
    private const string SecretValue = "local-client-value-4b1e";

    private readonly LocalApiTestFactory _api;
    private readonly LocalWebTestFactory _web;

    public LocalVaultClientsTests(LocalApiTestFactory api, LocalWebTestFactory web)
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

    [Fact]
    public async Task LocalClients_CreateListRevealDelete_Work()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web.Services, LocalPrincipalFactory.Create(LocalAccounts.UserOne, "test"));

        var folder = Succeeded(await circuit.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Local folder" }, cancellationToken));
        var created = Succeeded(await circuit.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Local login", Value = SecretValue, FolderId = folder.Id },
            cancellationToken));
        var listed = Succeeded(await circuit.VaultSecrets.ListSecretsAsync(folder.Id, false, null, 50, null, cancellationToken));
        Assert.Equal(created.Id, Assert.Single(listed.Items).Id);
        var revealed = Succeeded(await circuit.VaultSecrets.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken));
        Assert.Equal(SecretValue, revealed.Value);

        var deleted = await circuit.VaultSecrets.DeleteSecretAsync(created.Id, created.ConcurrencyToken, cancellationToken);

        Assert.Equal(ApiCallStatus.Success, deleted.Status);
        Assert.Equal(ApiCallStatus.NotFound, (await circuit.VaultSecrets.GetSecretAsync(created.Id, cancellationToken)).Status);
        Assert.Contains(Succeeded(await circuit.RecycleBin.ListAsync(50, null, cancellationToken)).Items, item => item.Id == created.Id);
        Assert.Empty(_api.StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task LocalClients_AdminOneOnUserOnesSecret_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = WebCircuit.For(_web.Services, LocalPrincipalFactory.Create(LocalAccounts.UserOne, "test"));
        using var admin = WebCircuit.For(_web.Services, LocalPrincipalFactory.Create(LocalAccounts.AdminOne, "test"));
        var secret = Succeeded(await owner.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Owned", Value = SecretValue },
            cancellationToken));
        var folder = Succeeded(await owner.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Owned folder" }, cancellationToken));

        Assert.Equal(ApiCallStatus.NotFound, (await admin.VaultSecrets.GetSecretAsync(secret.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await admin.VaultSecrets.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await admin.SecretHistory.ListVersionsAsync(secret.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await admin.VaultFolders.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "Taken" }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await admin.VaultSecrets.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken, cancellationToken)).Status);
        Assert.Empty(Succeeded(await admin.VaultSecrets.ListSecretsAsync(null, false, null, 50, null, cancellationToken)).Items);
        Assert.Equal(SecretValue, Succeeded(await owner.VaultSecrets.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken)).Value);
    }

    private static T Succeeded<T>(ApiResponse<T> response)
    {
        Assert.Equal(ApiCallStatus.Success, response.Status);
        return response.Value!;
    }
}
