// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(LocalModeEnvironmentGroup))]
[Trait("Category", "Security")]
public sealed class LocalModeEnvironmentTests(LocalApiTestFactory factory)
{
    private const string TenantVariable = "AzureAd__TenantId";
    private const string ClientVariable = "AzureAd__ClientId";

    [Fact]
    public void LocalApiTestFactory_BuiltHost_IsInLocalMode()
    {
        EntraModeAssertion.AssertLocal(factory.Services);
    }

    [Fact]
    public void LocalApiTestFactory_WithEntraVariablesInTheEnvironment_IsStillInLocalMode()
    {
        var tenant = Environment.GetEnvironmentVariable(TenantVariable);
        var client = Environment.GetEnvironmentVariable(ClientVariable);
        try
        {
            Environment.SetEnvironmentVariable(TenantVariable, "5f0c1d2e-4b7a-4c3d-9e8f-00000000a001");
            Environment.SetEnvironmentVariable(ClientVariable, "5f0c1d2e-4b7a-4c3d-9e8f-00000000a002");
            using var restarted = factory.WithWebHostBuilder(_ => { });

            EntraModeAssertion.AssertLocal(restarted.Services);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TenantVariable, tenant);
            Environment.SetEnvironmentVariable(ClientVariable, client);
        }
    }
}
