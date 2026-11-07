// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using NSubstitute;
using SharpVault.Application.Features.OneTimeLinks.Common;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class ConfiguredOneTimeLinkPolicyTests
{
    private readonly IOptionsMonitor<OneTimeLinkOptions> _monitor = Substitute.For<IOptionsMonitor<OneTimeLinkOptions>>();

    [Fact]
    public async Task Get_ReflectsTheCurrentOptions()
    {
        _monitor.CurrentValue.Returns(new OneTimeLinkOptions { Enabled = true, MaxLifetime = TimeSpan.FromDays(2) });
        var policy = new ConfiguredOneTimeLinkPolicy(_monitor);

        var settings = await policy.GetAsync(TestContext.Current.CancellationToken);

        Assert.True(settings.Enabled);
        Assert.Equal(TimeSpan.FromDays(2), settings.MaxLifetime);
    }

    [Fact]
    public async Task Get_OptionsChanged_ReturnsTheNewValue()
    {
        _monitor.CurrentValue.Returns(new OneTimeLinkOptions { Enabled = false, MaxLifetime = TimeSpan.FromDays(7) });
        var policy = new ConfiguredOneTimeLinkPolicy(_monitor);
        var before = await policy.GetAsync(TestContext.Current.CancellationToken);

        _monitor.CurrentValue.Returns(new OneTimeLinkOptions { Enabled = true, MaxLifetime = TimeSpan.FromHours(3) });
        var after = await policy.GetAsync(TestContext.Current.CancellationToken);

        Assert.False(before.Enabled);
        Assert.True(after.Enabled);
        Assert.Equal(TimeSpan.FromHours(3), after.MaxLifetime);
    }
}
