// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Common;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Users;

public sealed class ReasonRulesTests
{
    private const char Letter = 'a';

    private static string Text(int length) => new(Letter, length);

    private static void AssertOnly(string? reason, string message)
    {
        var result = ServerReasonRules.Validate(reason);

        Assert.False(result.IsValid);
        Assert.Equal(message, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void AdminReason_ExactlyMinLength_Passes() =>
        Assert.True(ServerReasonRules.Accepts(Text(ReasonRules.MinLength)));

    [Fact]
    public void AdminReason_OneBelowMinLength_FailsTooShort() =>
        AssertOnly(Text(ReasonRules.MinLength - 1), UserAdminMessages.ReasonTooShort);

    [Fact]
    public void AdminReason_ExactlyMaxLength_Passes() =>
        Assert.True(ServerReasonRules.Accepts(Text(ReasonRules.MaxLength)));

    [Fact]
    public void AdminReason_OneOverMaxLength_FailsTooLong() =>
        AssertOnly(Text(ReasonRules.MaxLength + 1), UserAdminMessages.ReasonTooLong);

    [Fact]
    public void AdminReason_OverMaxLengthWithControlCharacter_ReportsOnlyTooLong() =>
        AssertOnly(Text(ReasonRules.MaxLength + 1) + "\u0000", UserAdminMessages.ReasonTooLong);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n ")]
    [InlineData(null)]
    public void AdminReason_EmptyOrWhitespace_FailsTooShort(string? reason) =>
        AssertOnly(reason, UserAdminMessages.ReasonTooShort);

    [Fact]
    public void AdminReason_LeadingAndTrailingSpacesDoNotCountTowardMinimum() =>
        AssertOnly("  " + Text(ReasonRules.MinLength - 2) + "  ", UserAdminMessages.ReasonTooShort);

    [Fact]
    public void AdminReason_LeadingAndTrailingSpacesAroundMinimumText_Passes() =>
        Assert.True(ServerReasonRules.Accepts("  " + Text(ReasonRules.MinLength) + "  "));

    [Fact]
    public void AdminReason_TrailingNewlineAroundMinimumText_Passes() =>
        Assert.True(ServerReasonRules.Accepts(Text(ReasonRules.MinLength) + "\n"));

    [Fact]
    public void AdminReason_TrailingCrLfAroundMinimumText_Passes() =>
        Assert.True(ServerReasonRules.Accepts(Text(ReasonRules.MinLength) + "\r\n"));

    [Fact]
    public void AdminReason_TrailingNewlineDoesNotCountTowardMinimum() =>
        AssertOnly(Text(ReasonRules.MinLength - 1) + "\n", UserAdminMessages.ReasonTooShort);

    [Fact]
    public void AdminReason_NonBreakingSpaceIsTrimmedLikeAnySpace() =>
        AssertOnly(Text(ReasonRules.MinLength - 1) + "\u00A0", UserAdminMessages.ReasonTooShort);

    [Fact]
    [Trait("Category", "Security")]
    public void AdminReason_EmbeddedLineBreaksAndTabs_Pass() =>
        Assert.True(ServerReasonRules.Accepts("First line of the reason\nSecond line\tmore detail"));

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("\u0000")]
    [InlineData("\u0001")]
    [InlineData("\u0007")]
    [InlineData("\u001B")]
    [InlineData("\u007F")]
    [InlineData("\u0085")]
    public void AdminReason_ControlCharacterInside_FailsNotAllowed(string control) =>
        AssertOnly(Text(10) + control + Text(10), UserAdminMessages.ReasonNotAllowed);

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("{HIGH}")]
    [InlineData("{LOW}")]
    public void AdminReason_LoneSurrogate_FailsNotAllowed(string surrogateToken) =>
        AssertOnly(Text(10) + ReasonText.Expand(surrogateToken) + Text(10), UserAdminMessages.ReasonNotAllowed);

    [Fact]
    [Trait("Category", "Security")]
    public void AdminReason_ValidSurrogatePair_Passes() =>
        Assert.True(ServerReasonRules.Accepts(Text(ReasonRules.MinLength - 2) + ReasonText.Expand("{HIGH}{LOW}")));
}
