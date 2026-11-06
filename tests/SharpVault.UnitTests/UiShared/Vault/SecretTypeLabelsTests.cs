// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class SecretTypeLabelsTests
{
    [Fact]
    public void Parse_EveryOptionValue_RoundTrips()
    {
        foreach (var option in SecretTypeLabels.Options)
        {
            var expected = Enum.Parse<SecretTypeDto>(option.Value);
            Assert.Equal(expected, SecretTypeLabels.Parse(option.Value, SecretTypeDto.Other == expected ? SecretTypeDto.Login : SecretTypeDto.Other));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("login")]
    [InlineData("99")]
    [InlineData("0")]
    public void Parse_UnknownValue_UsesTheFallback(string? value)
    {
        Assert.Equal(SecretTypeDto.Other, SecretTypeLabels.Parse(value, SecretTypeDto.Other));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("Login,Card")]
    [InlineData("Login,SecureNote")]
    public void Parse_NumericOrCombinedValue_UsesTheFallback(string value)
    {
        Assert.Equal(SecretTypeDto.Other, SecretTypeLabels.Parse(value, SecretTypeDto.Other));
    }
}
