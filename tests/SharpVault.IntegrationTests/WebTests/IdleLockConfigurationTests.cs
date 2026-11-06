// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.IntegrationTests.Factories;
using SharpVault.UI.Shared.Services.Idle;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
public sealed class IdleLockConfigurationTests(WebTestFactory factory)
{
    [Fact]
    public void Web_ConfiguredIdleTimeout_ReachesTheOptions()
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting("IdleLock:Timeout", "00:05:00"));

        var options = configured.Services.GetRequiredService<IOptions<IdleLockOptions>>();

        Assert.Equal(TimeSpan.FromMinutes(5), options.Value.Timeout);
    }

    [Fact]
    public void Web_NoIdleTimeoutConfigured_UsesTheFifteenMinuteDefault()
    {
        var options = factory.Services.GetRequiredService<IOptions<IdleLockOptions>>();

        Assert.Equal(TimeSpan.FromMinutes(15), options.Value.Timeout);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("00:00:30")]
    [InlineData("00:00:00")]
    [InlineData("09:00:00")]
    [InlineData("-00:15:00")]
    public void Web_InvalidIdleTimeout_FailsStartup(string timeout)
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting("IdleLock:Timeout", timeout));

        var exception = Assert.ThrowsAny<Exception>(() => configured.Services);

        Assert.Contains("IdleLock:Timeout", exception.ToString(), StringComparison.Ordinal);
    }
}
