// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Authentication.Local;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.Vault;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LogIdLeakageTests : IAsyncLifetime
{
    private readonly LocalApiTestFactory _api;
    private readonly LocalWebTestFactory _web;

    public LogIdLeakageTests(LocalApiTestFactory api, LocalWebTestFactory web)
    {
        _api = api;
        _web = web;
        _web.UseApi(api);
    }

    public async ValueTask InitializeAsync()
    {
        await _api.ResetAndSeedAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SecretAndFolderRequests_NeverPutTheirIdsInAnyRecordOrScopeAtInformationOrBelow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web.Services, LocalPrincipalFactory.Create(LocalAccounts.UserOne, "test"));
        var folder = Succeeded(await circuit.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Leak folder" }, cancellationToken));
        var secret = Succeeded(await circuit.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Leak login", Value = "leak-value-71c2", FolderId = folder.Id },
            cancellationToken));

        Succeeded(await circuit.VaultSecrets.GetSecretAsync(secret.Id, cancellationToken));
        Succeeded(await circuit.VaultSecrets.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken));
        Succeeded(await circuit.SecretHistory.ListVersionsAsync(secret.Id, cancellationToken));

        var records = _web.OpenTelemetryLogs.Records.Concat(_api.OpenTelemetryLogs.Records)
            .Where(record => record.Level <= LogLevel.Information)
            .ToList();
        Assert.NotEmpty(records);
        foreach (var id in new[] { secret.Id, folder.Id })
        {
            var leaks = records.Where(record => record.Text.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)
                || record.Text.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase));
            Assert.Empty(leaks.Select(record => $"{record.Category} {record.Level}").Distinct());
        }
    }

    private static T Succeeded<T>(ApiResponse<T> response)
    {
        Assert.Equal(ApiCallStatus.Success, response.Status);
        return response.Value!;
    }
}
