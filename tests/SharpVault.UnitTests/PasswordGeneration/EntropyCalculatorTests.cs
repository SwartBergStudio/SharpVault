// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.PasswordGeneration;

namespace SharpVault.UnitTests.PasswordGeneration;

public sealed class EntropyCalculatorTests
{
    private const double Tolerance = 1e-3;

    [Theory]
    [InlineData(new[] { 26 }, 20, false, 94.0088)]
    [InlineData(new[] { 26, 10 }, 8, true, 41.2484)]
    [InlineData(new[] { 26, 10 }, 8, false, 41.3594)]
    [InlineData(new[] { 26, 26, 10, 32 }, 20, true, 130.9259)]
    [InlineData(new[] { 26, 26, 10, 32 }, 8, true, 51.3183)]
    public void PasswordBits_GoldenValues_MatchWithin0001(int[] sizes, int length, bool requireEverySet, double expected)
    {
        var bits = EntropyCalculator.PasswordBits(sizes, length, requireEverySet);

        Assert.InRange(bits, expected - Tolerance, expected + Tolerance);
    }

    [Theory]
    [InlineData(6, false, 77.5489)]
    [InlineData(6, true, 83.4558)]
    public void PassphraseBits_GoldenValues_Match(int words, bool includeNumber, double expected)
    {
        var bits = EntropyCalculator.PassphraseBits(words, includeNumber);

        Assert.InRange(bits, expected - Tolerance, expected + Tolerance);
    }

    [Fact]
    public void PasswordBits_RequireEverySet_IsBelowUnconstrained()
    {
        var constrained = EntropyCalculator.PasswordBits([26, 26, 10, 32], 12, true);
        var unconstrained = EntropyCalculator.PasswordBits([26, 26, 10, 32], 12, false);

        Assert.True(constrained < unconstrained);
    }

    [Fact]
    public void PasswordBits_NullSizes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EntropyCalculator.PasswordBits(null!, 8, false));
    }

    [Theory]
    [InlineData(0, PasswordStrength.VeryWeak)]
    [InlineData(39.99, PasswordStrength.VeryWeak)]
    [InlineData(40, PasswordStrength.Weak)]
    [InlineData(59.99, PasswordStrength.Weak)]
    [InlineData(60, PasswordStrength.Reasonable)]
    [InlineData(79.99, PasswordStrength.Reasonable)]
    [InlineData(80, PasswordStrength.Strong)]
    [InlineData(99.99, PasswordStrength.Strong)]
    [InlineData(100, PasswordStrength.VeryStrong)]
    [InlineData(500, PasswordStrength.VeryStrong)]
    public void StrengthOf_Boundaries_MapToExpectedLevel(double bits, PasswordStrength expected)
    {
        Assert.Equal(expected, EntropyCalculator.StrengthOf(bits));
        Assert.Equal(expected, PasswordGenerator.StrengthOf(bits));
    }
}
