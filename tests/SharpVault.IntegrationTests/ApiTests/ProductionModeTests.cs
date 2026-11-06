// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Hosting;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(ProductionTestGroup))]
[Trait("Category", "Security")]
public sealed class ProductionModeTests(ProductionApiTestFactory factory)
{
    private const string EntraRequired = "AzureAd:TenantId and AzureAd:ClientId are required outside the Development environment.";
    private const string ClientIdRequired = "AzureAd:ClientId is required when AzureAd:TenantId is set.";

    [Fact]
    public void ProductionApiTestFactory_BuiltHost_IsInEntraMode()
    {
        EntraModeAssertion.Assert(factory.Services);
    }

    [Fact]
    public void Startup_ProductionWithoutEntraSettings_FailsWithoutLocalFallback()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AzureAd:TenantId", string.Empty);
            builder.UseSetting("AzureAd:ClientId", string.Empty);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EntraRequired, invalid.Message);
    }

    [Fact]
    public void Startup_ProductionWithOnlyTenantId_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("AzureAd:ClientId", string.Empty));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(ClientIdRequired, invalid.Message);
    }

    [Fact]
    public void Startup_StagingWithoutEntraSettings_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("AzureAd:TenantId", string.Empty);
            builder.UseSetting("AzureAd:ClientId", string.Empty);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EntraRequired, invalid.Message);
    }
}
