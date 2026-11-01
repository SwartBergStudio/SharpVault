// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Access;

namespace SharpVault.UnitTests.Application.Users;

[Trait("Category", "Security")]
public sealed class UserAccessOptionsValidatorTests
{
    private readonly UserAccessOptionsValidator _validator = new();

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        var result = _validator.Validate(null, new UserAccessOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30_000)]
    public void Validate_CacheTtlBounds_AcceptsZeroAndThirtySeconds(int milliseconds)
    {
        var result = _validator.Validate(null, new UserAccessOptions { CacheTtl = TimeSpan.FromMilliseconds(milliseconds) });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(-1)]
    [InlineData(30_001)]
    [InlineData(31_000)]
    public void Validate_CacheTtlOutOfRange_Fails(int milliseconds)
    {
        var result = _validator.Validate(null, new UserAccessOptions { CacheTtl = TimeSpan.FromMilliseconds(milliseconds) });

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures!);
        Assert.Contains("UserAccess:CacheTtl", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CacheTtlMaxValue_Fails()
    {
        var result = _validator.Validate(null, new UserAccessOptions { CacheTtl = TimeSpan.MaxValue });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1_000_000)]
    public void Validate_CacheMaxEntriesBounds_Accepted(int entries)
    {
        var result = _validator.Validate(null, new UserAccessOptions { CacheMaxEntries = entries });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    [InlineData(int.MaxValue)]
    public void Validate_CacheMaxEntriesOutOfRange_Fails(int entries)
    {
        var result = _validator.Validate(null, new UserAccessOptions { CacheMaxEntries = entries });

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures!);
        Assert.Contains("UserAccess:CacheMaxEntries", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(86_400)]
    public void Validate_SignInRecordIntervalBounds_Accepted(int seconds)
    {
        var result = _validator.Validate(null, new UserAccessOptions { SignInRecordInterval = TimeSpan.FromSeconds(seconds) });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(0)]
    [InlineData(-60)]
    [InlineData(86_401)]
    public void Validate_SignInRecordIntervalOutOfRange_Fails(int seconds)
    {
        var result = _validator.Validate(null, new UserAccessOptions { SignInRecordInterval = TimeSpan.FromSeconds(seconds) });

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures!);
        Assert.Contains("UserAccess:SignInRecordInterval", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_EveryValueOutOfRange_ReportsEveryFailureWithoutValues()
    {
        var options = new UserAccessOptions
        {
            CacheTtl = TimeSpan.FromSeconds(9999),
            CacheMaxEntries = 7,
            SignInRecordInterval = TimeSpan.FromSeconds(3),
        };

        var result = _validator.Validate(null, options);

        Assert.Equal(3, result.Failures!.Count());
        Assert.All(result.Failures!, failure =>
        {
            Assert.DoesNotContain("9999", failure, StringComparison.Ordinal);
            Assert.DoesNotContain(" 7 ", failure, StringComparison.Ordinal);
        });
    }
}
