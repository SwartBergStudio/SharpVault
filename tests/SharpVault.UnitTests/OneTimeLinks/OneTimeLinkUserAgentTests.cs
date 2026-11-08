// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Access;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkUserAgentTests
{
    private const int UserAgentMaxLength = UserProfileText.UserAgentMaxLength;

    [Fact]
    public void NormalizeUserAgent_ControlCharacters_AreRemoved()
    {
        var normalized = UserProfileText.NormalizeUserAgent("Agent\u0000/1.0\r\n\tX-Injected: yes\u0007");

        Assert.NotNull(normalized);
        Assert.DoesNotContain("\r", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\t", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0000", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", normalized, StringComparison.Ordinal);
        Assert.Contains("Agent/1.0", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeUserAgent_TooLong_IsTruncated()
    {
        var normalized = UserProfileText.NormalizeUserAgent(new string('a', 4000));

        Assert.NotNull(normalized);
        Assert.Equal(UserAgentMaxLength, normalized.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0000\u0001\r\n")]
    public void NormalizeUserAgent_Empty_ReturnsNull(string? value)
    {
        Assert.Null(UserProfileText.NormalizeUserAgent(value));
    }
}
