// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using SharpVault.Authentication.Local;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class ClientAddressAuditLocalTests : IAsyncLifetime
{
    private const string SecretValue = "local-client-address-value-9b2e";

    private readonly LocalApiTestFactory _api;
    private readonly LocalWebTestFactory _web;

    public ClientAddressAuditLocalTests(LocalApiTestFactory api, LocalWebTestFactory web)
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
    public async Task ClientAddress_LocalModeProofVerifiesWithTheSameKey_RevealRowHoldsBrowserAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var principal = LocalPrincipalFactory.Create(LocalAccounts.UserOne, "test");
        ClientAddressClaim.AddTo(principal, IPAddress.Parse(TestAddresses.Browser));
        using var circuit = WebCircuit.For(_web.Services, principal);
        var created = await circuit.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Local reveal", Value = SecretValue },
            cancellationToken);
        Assert.Equal(ApiCallStatus.Success, created.Status);

        var revealed = await circuit.VaultSecrets.RevealSecretAsync(
            created.Value!.Id,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View },
            cancellationToken);

        Assert.Equal(ApiCallStatus.Success, revealed.Status);
        var rows = await AuditRows.ListAsync(_api.StorageAdminClient.GetTableClient(TableNames.AuditLog), cancellationToken);
        var row = Assert.Single(rows, candidate => candidate.GetString("EventType") == nameof(AuditEventType.SecretValueRevealed));
        Assert.Equal(TestAddresses.Browser, row.GetString("IpAddress"));
    }
}
