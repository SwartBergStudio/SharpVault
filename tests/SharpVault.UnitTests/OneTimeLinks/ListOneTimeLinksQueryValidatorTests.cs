// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.OneTimeLinks.Queries;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class ListOneTimeLinksQueryValidatorTests
{
    private const string ValidToken = "0123456789abcdef0123456789abcdef";

    private static readonly ListOneTimeLinksQueryValidator Validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Validate_PageSizeAtBounds_Passes(int pageSize)
    {
        Assert.True(Validator.Validate(new ListOneTimeLinksQuery(pageSize, null)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_PageSizeOutOfRange_Fails(int pageSize)
    {
        Assert.False(Validator.Validate(new ListOneTimeLinksQuery(pageSize, null)).IsValid);
    }

    [Fact]
    public void Validate_NoToken_Passes()
    {
        Assert.True(Validator.Validate(new ListOneTimeLinksQuery(50, null)).IsValid);
    }

    [Fact]
    public void Validate_ValidToken_Passes()
    {
        Assert.True(Validator.Validate(new ListOneTimeLinksQuery(50, ValidToken)).IsValid);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef/")]
    [InlineData("0123456789abcdef0123456789abcdef\n")]
    [InlineData("0123456789abcdef0123456789abcdef' or PartitionKey ne '")]
    public void Validate_MalformedToken_Fails(string token)
    {
        Assert.False(Validator.Validate(new ListOneTimeLinksQuery(50, token)).IsValid);
    }

    [Fact]
    public void Validate_TokenWithTrailingNewline_Fails()
    {
        Assert.False(Validator.Validate(new ListOneTimeLinksQuery(50, ValidToken + "\n")).IsValid);
    }
}
