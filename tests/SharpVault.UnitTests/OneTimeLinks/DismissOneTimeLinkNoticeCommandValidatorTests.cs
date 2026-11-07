// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.OneTimeLinks.Commands;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class DismissOneTimeLinkNoticeCommandValidatorTests
{
    private const string ValidNoticeId = "n_0000000000000000001_0123456789abcdef0123456789abcdef";

    private static readonly DismissOneTimeLinkNoticeCommandValidator Validator = new();

    [Fact]
    public void Validate_WellFormedNoticeId_Passes()
    {
        Assert.True(Validator.Validate(new DismissOneTimeLinkNoticeCommand(ValidNoticeId)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0000000000000000001_0123456789abcdef0123456789abcdef")]
    [InlineData("n_000000000000000001_0123456789abcdef0123456789abcdef")]
    [InlineData("n_0000000000000000001_0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("n_0000000000000000001/0123456789abcdef0123456789abcdef")]
    [InlineData("n_0000000000000000001_0123456789abcdef0123456789abcdef\\")]
    [InlineData("n_0000000000000000001_0123456789abcdef0123456789abcdef#")]
    [InlineData("n_0000000000000000001_0123456789abcdef0123456789abcdef?")]
    [InlineData("n_0000000000000000001_0123456789abcdef0123456789abcdef\u0007")]
    [InlineData("n_0000000000000000001' or PartitionKey ne '")]
    public void Validate_MalformedNoticeId_Fails(string noticeId)
    {
        Assert.False(Validator.Validate(new DismissOneTimeLinkNoticeCommand(noticeId)).IsValid);
    }

    [Fact]
    public void Validate_NoticeIdWithTrailingNewline_Fails()
    {
        Assert.False(Validator.Validate(new DismissOneTimeLinkNoticeCommand(ValidNoticeId + "\n")).IsValid);
    }
}
