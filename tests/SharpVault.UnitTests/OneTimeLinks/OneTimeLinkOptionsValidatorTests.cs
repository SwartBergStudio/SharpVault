// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.OneTimeLinks.Common;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkOptionsValidatorTests
{
    private static readonly OneTimeLinkOptionsValidator Validator = new();

    [Fact]
    public void Defaults_AreDisabledAndSevenDays()
    {
        var options = new OneTimeLinkOptions();

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromDays(7), options.MaxLifetime);
    }

    [Fact]
    public void Validate_MaxLifetimeOverSevenDays_Fails()
    {
        var result = Validator.Validate(null, Options(maxLifetime: TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1)));

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_MaxLifetimeUnderOneHour_Fails()
    {
        var result = Validator.Validate(null, Options(maxLifetime: TimeSpan.FromMinutes(59)));

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_MaxLifetimeAtBothBounds_Passes()
    {
        Assert.True(Validator.Validate(null, Options(maxLifetime: TimeSpan.FromHours(1))).Succeeded);
        Assert.True(Validator.Validate(null, Options(maxLifetime: TimeSpan.FromDays(7))).Succeeded);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(61 * 60)]
    public void Validate_PurgeIntervalOutOfRange_Fails(int seconds)
    {
        var result = Validator.Validate(null, Options(purgeInterval: TimeSpan.FromSeconds(seconds)));

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(31 * 60)]
    public void Validate_LeaseDurationOutOfRange_Fails(int seconds)
    {
        var result = Validator.Validate(null, Options(leaseDuration: TimeSpan.FromSeconds(seconds)));

        Assert.True(result.Failed);
    }

    private static OneTimeLinkOptions Options(
        TimeSpan? maxLifetime = null,
        TimeSpan? purgeInterval = null,
        TimeSpan? leaseDuration = null) => new()
    {
        MaxLifetime = maxLifetime ?? TimeSpan.FromDays(7),
        PurgeInterval = purgeInterval ?? TimeSpan.FromMinutes(5),
        PurgeLeaseDuration = leaseDuration ?? TimeSpan.FromMinutes(5),
    };
}
