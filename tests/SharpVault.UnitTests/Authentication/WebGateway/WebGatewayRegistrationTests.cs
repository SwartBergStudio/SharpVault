// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.UnitTests.Authentication.WebGateway;

[Trait("Category", "Security")]
public sealed class WebGatewayRegistrationTests
{
    [Fact]
    public async Task Production_NoWebGatewayKey_WebStopsAtStartup()
    {
        using var host = BuildHost(Environments.Production, key: null);

        var exception = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
        Assert.Contains("WebGateway:Key is required", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_NoWebGatewayKey_WebStarts()
    {
        using var host = BuildHost(Environments.Development, key: null);

        var exception = await Record.ExceptionAsync(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    private static IHost BuildHost(string environment, string? key)
    {
        var settings = new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            Configuration = new ConfigurationManager(),
            DisableDefaults = true,
        };
        settings.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["WebGateway:Key"] = key });
        var builder = Host.CreateApplicationBuilder(settings);
        builder.AddWebGateway();
        return builder.Build();
    }
}
