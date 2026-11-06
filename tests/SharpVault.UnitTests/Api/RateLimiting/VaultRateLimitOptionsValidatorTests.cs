// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Api.RateLimiting;

namespace SharpVault.UnitTests.Api.RateLimiting;

public sealed class VaultRateLimitOptionsValidatorTests
{
    private static readonly VaultRateLimitOptionsValidator Validator = new();

    [Fact]
    public void Defaults_AreValidAndMatchThePlan()
    {
        var options = new VaultRateLimitOptions();

        Assert.True(Validator.Validate(null, options).Succeeded);
        Assert.Equal(600, options.RequestsPerMinute);
        Assert.Equal(60, options.RevealsPerMinute);
        Assert.Equal(120, options.ShareWritesPerMinute);
        Assert.Equal(30, options.DirectorySearchesPerMinute);
        Assert.Equal("VaultRateLimits", VaultRateLimitOptions.SectionName);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MinValue, false)]
    public void RequestsPerMinute_Range(int value, bool valid)
    {
        var result = Validator.Validate(null, new VaultRateLimitOptions { RequestsPerMinute = value });

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.Contains(result.Failures!, failure => failure.Contains("RequestsPerMinute", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Failures!, failure => failure.Contains("RevealsPerMinute", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    [InlineData(-1, false)]
    public void RevealsPerMinute_Range(int value, bool valid)
    {
        var result = Validator.Validate(null, new VaultRateLimitOptions { RevealsPerMinute = value });

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.Contains(result.Failures!, failure => failure.Contains("RevealsPerMinute", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Failures!, failure => failure.Contains("RequestsPerMinute", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    [InlineData(-1, false)]
    public void ShareWritesPerMinute_Range(int value, bool valid)
    {
        var result = Validator.Validate(null, new VaultRateLimitOptions { ShareWritesPerMinute = value });

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            var failure = Assert.Single(result.Failures!);
            Assert.Contains("ShareWritesPerMinute", failure, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    [InlineData(-1, false)]
    public void DirectorySearchesPerMinute_Range(int value, bool valid)
    {
        var result = Validator.Validate(null, new VaultRateLimitOptions { DirectorySearchesPerMinute = value });

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            var failure = Assert.Single(result.Failures!);
            Assert.Contains("DirectorySearchesPerMinute", failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BothInvalid_ReportsBothFailures()
    {
        var result = Validator.Validate(null, new VaultRateLimitOptions { RequestsPerMinute = 0, RevealsPerMinute = 0 });

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Validator.Validate(null, null!));
    }

    [Fact]
    public void PolicyNames_AreTheDocumentedConstants()
    {
        Assert.Equal("vault", RateLimitPolicies.Vault);
        Assert.Equal("vault-reveal", RateLimitPolicies.Reveal);
    }
}
