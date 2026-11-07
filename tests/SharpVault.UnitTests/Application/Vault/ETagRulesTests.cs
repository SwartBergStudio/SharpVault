// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class ETagRulesTests
{
    private static readonly ETagValidator Validator = new();

    private static bool Valid(string? value) => Validator.Validate(new ETagModel(value)).IsValid;

    [Fact]
    public void ValidIfMatch_AzureFormat_Passes()
    {
        const string value = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";

        Assert.True(Valid(value));
        Assert.True(ETagRules.IsValid(value));
    }

    [Fact]
    public void ValidIfMatch_Wildcard_Fails()
    {
        Assert.False(Valid("*"));
    }

    [Theory]
    [InlineData("\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"")]
    [InlineData("abc")]
    public void ValidIfMatch_StrongOrBareETag_Fails(string value)
    {
        Assert.False(Valid(value));
    }

    [Theory]
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
    [Trait("Category", "Security")]
    [InlineData("W/\"datetime'abc'\" or 1=1")]
    [InlineData("W/\"datetime'a\"b'\"")]
    [InlineData("W/\"datetime'abc'\"\n")]
    [InlineData("W/\"datetime'abc'\"\r\nX-Injected: 1")]
    public void ValidIfMatch_InjectedQuoteOrLineBreak_Fails(string value)
    {
        Assert.False(Valid(value));
    }

    private sealed record ETagModel(string? Value);

    private sealed class ETagValidator : AbstractValidator<ETagModel>
    {
        public ETagValidator() => RuleFor(model => model.Value).ValidIfMatch();
    }
}
