// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.UnitTests.Authentication.WebGateway;

[Trait("Category", "Security")]
public sealed class WebGatewayOptionsValidatorTests
{
    private const string ShortKey = "short-secret-value";

    private static readonly string MinimumKey = new('k', WebGatewayOptions.MinimumKeyLength);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RequiredAndEmpty_Fails(string? key)
    {
        var result = Validate(key, keyRequired: true);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_RequiredAndWhitespace_Fails()
    {
        var result = Validate("\t \n", keyRequired: true);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ShortKey_FailsEvenWhenNotRequired()
    {
        var result = Validate(ShortKey, keyRequired: false);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ShortKey_MessageNeverEchoesTheKey()
    {
        var result = Validate(ShortKey, keyRequired: true);

        Assert.True(result.Failed);
        Assert.DoesNotContain(ShortKey, result.FailureMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_NotRequiredAndEmpty_Passes(string? key)
    {
        var result = Validate(key, keyRequired: false);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MinimumLengthKey_Passes()
    {
        var result = Validate(MinimumKey, keyRequired: true);

        Assert.True(result.Succeeded);
    }

    private static ValidateOptionsResult Validate(string? key, bool keyRequired) =>
        new WebGatewayOptionsValidator(keyRequired).Validate(null, new WebGatewayOptions { Key = key });
}
