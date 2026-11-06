// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class ETagRulesTests
{
    private static readonly ETagValidator Validator = new();

    private static bool Valid(string? value) => Validator.Validate(new ETagModel(value)).IsValid;

    [Theory]
    [InlineData("W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"")]
    [InlineData("W/\"datetime'2026-11-05T02%3A03%3A07.2843881Z'\"")]
    public void ValidIfMatch_AzureFormat_Passes(string value)
    {
        Assert.True(Valid(value));
        Assert.True(ETagRules.IsValid(value));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/*")]
    public void ValidIfMatch_Wildcard_Fails(string value)
    {
        Assert.False(Valid(value));
    }

    [Theory]
    [InlineData("\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"")]
    [InlineData("\"abc\"")]
    [InlineData("abc")]
    public void ValidIfMatch_StrongETag_Fails(string value)
    {
        Assert.False(Valid(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ValidIfMatch_Empty_Fails(string? value)
    {
        Assert.False(Valid(value));
    }

    [Fact]
    public void ValidIfMatch_Over100Chars_Fails()
    {
        var value = "W/\"datetime'" + new string('A', 100) + "'\"";

        Assert.False(Valid(value));
    }

    [Fact]
    public void ValidIfMatch_Exactly100Chars_Passes()
    {
        var prefix = "W/\"datetime'";
        var suffix = "'\"";
        var value = prefix + new string('A', 100 - prefix.Length - suffix.Length) + suffix;

        Assert.Equal(100, value.Length);
        Assert.True(Valid(value));
    }

    [Theory]
    [InlineData("W/\"datetime'abc'\" or 1=1")]
    [InlineData("W/\"datetime'a\"b'\"")]
    [InlineData("W/\"datetime'a'b'\"")]
    [InlineData("W/\"datetime'abc'\"\n")]
    [InlineData("W/\"datetime'abc'\"\r\nX-Injected: 1")]
    [InlineData("W/\"datetime''\"")]
    [InlineData("W/\"datetime'a b'\"")]
    public void ValidIfMatch_InjectedQuote_Fails(string value)
    {
        Assert.False(Valid(value));
    }

    [Fact]
    public void ValidIfMatch_Message_IsTheConstantInvalidETagText()
    {
        var result = Validator.Validate(new ETagModel("*"));

        Assert.Equal(VaultMessages.InvalidETag, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void ValidIfMatch_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ETagRules.ValidIfMatch<ETagModel, string?>(null!));
    }

    private sealed record ETagModel(string? Value);

    private sealed class ETagValidator : AbstractValidator<ETagModel>
    {
        public ETagValidator() => RuleFor(model => model.Value).ValidIfMatch();
    }
}
