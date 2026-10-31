// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using NSubstitute;
using SharpVault.Application.Features.Tenancy;

namespace SharpVault.UnitTests.Application.Tenancy;

[Trait("Category", "Security")]
public sealed class ConfiguredGuestAccessPolicyTests
{
    private readonly IOptionsMonitor<TenancyOptions> _monitor = Substitute.For<IOptionsMonitor<TenancyOptions>>();

    [Fact]
    public async Task AreGuestsAllowedAsync_Default_ReturnsFalse()
    {
        _monitor.CurrentValue.Returns(new TenancyOptions());

        var allowed = await new ConfiguredGuestAccessPolicy(_monitor).AreGuestsAllowedAsync(TestContext.Current.CancellationToken);

        Assert.False(allowed);
    }

    [Fact]
    public async Task AreGuestsAllowedAsync_Configured_ReturnsTrue()
    {
        _monitor.CurrentValue.Returns(new TenancyOptions { AllowGuests = true });

        var allowed = await new ConfiguredGuestAccessPolicy(_monitor).AreGuestsAllowedAsync(TestContext.Current.CancellationToken);

        Assert.True(allowed);
    }

    [Fact]
    public async Task AreGuestsAllowedAsync_ConfigurationChanges_ReadsCurrentValueEachCall()
    {
        var policy = new ConfiguredGuestAccessPolicy(_monitor);
        _monitor.CurrentValue.Returns(new TenancyOptions { AllowGuests = true });
        Assert.True(await policy.AreGuestsAllowedAsync(TestContext.Current.CancellationToken));

        _monitor.CurrentValue.Returns(new TenancyOptions { AllowGuests = false });

        Assert.False(await policy.AreGuestsAllowedAsync(TestContext.Current.CancellationToken));
    }
}
