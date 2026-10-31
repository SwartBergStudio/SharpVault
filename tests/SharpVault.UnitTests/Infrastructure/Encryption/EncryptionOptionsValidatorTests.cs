// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.Encryption;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

public sealed class EncryptionOptionsValidatorTests
{
    private const string ValidKeyId = "https://sharpvault-test.vault.azure.net/keys/sharpvault-kek";

    [Fact]
    public void Validate_ValidDevelopmentSettings_Succeeds()
    {
        var validator = new EncryptionOptionsValidator(HostEnvironments.Named("Development"));
        var options = new EncryptionOptions { DevelopmentKeyMaterial = TestEncryptionOptions.RandomMaterial() };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ValidProductionSettings_Succeeds()
    {
        var validator = new EncryptionOptionsValidator(HostEnvironments.Named("Production"));

        var result = validator.Validate(null, new EncryptionOptions { KeyVaultKeyId = ValidKeyId });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Dev")]
    public void Validate_NonDevelopmentWithDevelopmentProvider_Fails(string environmentName)
    {
        var validator = new EncryptionOptionsValidator(HostEnvironments.Named(environmentName));
        var options = new EncryptionOptions { Provider = KeyWrapProviderKind.Development };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, result.FailureMessage);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Validate_ProductionWithDevelopmentKeyMaterial_FailsWithoutEchoingValue()
    {
        const string material = "SV-MARKER-validator-0123456789abcdef0123456789";
        var validator = new EncryptionOptionsValidator(HostEnvironments.Named("Production"));
        var options = new EncryptionOptions { KeyVaultKeyId = ValidKeyId, DevelopmentKeyMaterial = material };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialNotAllowed, result.FailureMessage);
        Assert.DoesNotContain(material, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Validate_UndefinedProviderValue_Fails()
    {
        var validator = new EncryptionOptionsValidator(HostEnvironments.Named("Production"));
        var options = new EncryptionOptions { Provider = (KeyWrapProviderKind)7, KeyVaultKeyId = ValidKeyId };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(EncryptionMessageText.UnknownProvider, result.FailureMessage);
    }

    [Fact]
    public void Validate_NullOptions_ThrowsArgumentNullException()
    {
        var validator = new EncryptionOptionsValidator(HostEnvironments.Named("Development"));

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null, null!));
    }
}
