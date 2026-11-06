// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.Maintenance;

namespace SharpVault.UnitTests.Infrastructure.Maintenance;

public sealed class RecycleBinPurgeOptionsValidatorTests
{
    private static readonly RecycleBinPurgeOptionsValidator Validator = new();

    private static RecycleBinPurgeOptions Valid() => new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = Validator.Validate(null, Valid());

        Assert.True(result.Succeeded);
        Assert.Equal("RecycleBinPurge", RecycleBinPurgeOptions.SectionName);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    [InlineData(-1, false)]
    public void InitialDelay_Range(int seconds, bool valid)
    {
        var options = Valid();
        options.InitialDelay = TimeSpan.FromSeconds(seconds);

        var result = Validator.Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        Assert.Equal(!valid, result.Failures?.Contains(RecycleBinPurgeOptionsValidator.InitialDelayInvalid) == true);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(21600, true)]
    [InlineData(86400, true)]
    [InlineData(86401, false)]
    [InlineData(0, false)]
    public void Interval_Range(int seconds, bool valid)
    {
        var options = Valid();
        options.Interval = TimeSpan.FromSeconds(seconds);

        var result = Validator.Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        Assert.Equal(!valid, result.Failures?.Contains(RecycleBinPurgeOptionsValidator.IntervalInvalid) == true);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(300, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    [InlineData(0, false)]
    public void LeaseDuration_Range(int seconds, bool valid)
    {
        var options = Valid();
        options.LeaseDuration = TimeSpan.FromSeconds(seconds);

        var result = Validator.Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        Assert.Equal(!valid, result.Failures?.Contains(RecycleBinPurgeOptionsValidator.LeaseDurationInvalid) == true);
    }

    [Fact]
    public void Everything_Invalid_ReportsAllThreeFailures()
    {
        var options = new RecycleBinPurgeOptions
        {
            InitialDelay = TimeSpan.FromHours(2),
            Interval = TimeSpan.FromSeconds(1),
            LeaseDuration = TimeSpan.FromDays(1),
        };

        var result = Validator.Validate(null, options);

        Assert.Equal(3, result.Failures?.Count());
    }

    [Fact]
    public void NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Validator.Validate(null, null!));
    }
}
