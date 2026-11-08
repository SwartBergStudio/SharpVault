// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Sharing.Directory;

namespace SharpVault.UnitTests.Application.Sharing.UserDirectory;

public sealed class SearchDirectoryUsersQueryValidatorTests
{
    private readonly SearchDirectoryUsersQueryValidator _validator = new();

    [Theory]
    [InlineData("abc")]
    [InlineData("  abc  ")]
    [InlineData("o'brien")]
    [InlineData("anna@example.com")]
    [InlineData("Zoë Müller")]
    public void Validate_PlainTerm_Passes(string term)
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery(term));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ExactlyMaxLength_Passes()
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery(new string('a', DirectorySearchLimits.MaxTermLength)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MaxLengthPaddedWithSpaces_PassesBecauseTrimmed()
    {
        var term = "  " + new string('a', DirectorySearchLimits.MaxTermLength) + "  ";

        var result = _validator.Validate(new SearchDirectoryUsersQuery(term));

        Assert.True(result.IsValid);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Validate_OneOverMaxLength_Fails()
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery(new string('a', DirectorySearchLimits.MaxTermLength + 1)));

        Assert.False(result.IsValid);
        Assert.Equal("Search term must be between 3 and 100 characters.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    public void Validate_BelowMinLengthAfterTrim_Fails(string term)
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery(term));

        Assert.False(result.IsValid);
        Assert.Equal("Search term must be between 3 and 100 characters.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Validate_NullTerm_FailsAsRequired()
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery(null!));

        Assert.False(result.IsValid);
        Assert.Equal("Search term is required.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("a\"bc")]
    [InlineData("a\\bc")]
    [InlineData("a*c")]
    [InlineData("abc%22")]
    [InlineData("abc$filter=x")]
    [InlineData("abc OR (mail:x)")]
    [InlineData("abc;def")]
    [InlineData("abc\u0000def")]
    [InlineData("abc\ndef")]
    [InlineData("abc\u2028def")]
    [InlineData("abc‮def")]
    [InlineData("abc​def")]
    [InlineData("abc﻿def")]
    [InlineData("abc＂def")]
    [InlineData("abc😀def")]
    public void Validate_QuoteWildcardControlOrBidiCharacter_FailsWithConstantMessage(string term)
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery(term));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Search term contains characters that are not allowed.", error.ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Validate_InteriorNewlineBeforeTrailingNewline_Fails()
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery("abc\nabc\n"));

        Assert.False(result.IsValid);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Validate_Failure_NeverEchoesTheTerm()
    {
        var result = _validator.Validate(new SearchDirectoryUsersQuery("needle\"marker"));

        Assert.All(result.Errors, error => Assert.DoesNotContain("needle", error.ErrorMessage));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ToString_ReturnsTypeNameOnly()
    {
        var text = new SearchDirectoryUsersQuery("needle-marker").ToString();

        Assert.Equal(nameof(SearchDirectoryUsersQuery), text);
    }
}
