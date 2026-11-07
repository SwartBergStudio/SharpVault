// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class WebGatewayDevelopmentStartupTests(ApiTestFactory factory)
{
    [Fact]
    public void Development_NoWebGatewayKey_ApiStarts()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OneTimeLinks:Enabled", "false");
            builder.UseSetting("WebGateway:Key", string.Empty);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.Null(exception);
    }
}
