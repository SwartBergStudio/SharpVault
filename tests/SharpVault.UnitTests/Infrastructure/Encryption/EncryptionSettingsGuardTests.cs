// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.Encryption;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

[Trait("Category", "Security")]
public sealed class EncryptionSettingsGuardTests
{
    private const string ValidKeyId = "https://sharpvault-test.vault.azure.net/keys/sharpvault-kek";
    private const string Marker = "SV-MARKER-4d1f9b2c7a";
    private const string KeyIdHost = "https://x.vault.azure.net/keys/";

    public static TheoryData<string> InvalidKeyIds =>
    [
        "not a uri",
        "https://x.vault.azure.net/keys/k/0123abcd",
        KeyIdHost + new string('k', 513 - KeyIdHost.Length),
    ];

    [Fact]
    public void ResolveProvider_DevelopmentWithKeyMaterial_ReturnsDevelopment()
    {
        var options = new EncryptionOptions { DevelopmentKeyMaterial = TestEncryptionOptions.RandomMaterial() };

        var kind = EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named("Development"));

        Assert.Equal(KeyWrapProviderKind.Development, kind);
    }

    [Fact]
    public void ResolveProvider_DevelopmentWithoutKeyMaterial_ThrowsKeyMaterialMissing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(new EncryptionOptions(), HostEnvironments.Named("Development")));

        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialMissing, exception.Message);
    }

    [Fact]
    public void ResolveProvider_DevelopmentWithShortKeyMaterial_ThrowsWithoutEchoingValue()
    {
        var material = Marker + new string('x', 31 - Marker.Length);
        var options = new EncryptionOptions { DevelopmentKeyMaterial = material };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named("Development")));

        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialMissing, exception.Message);
        Assert.DoesNotContain(Marker, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveProvider_DevelopmentWithExplicitKeyVaultAndValidKeyId_ReturnsKeyVault()
    {
        var options = new EncryptionOptions { Provider = KeyWrapProviderKind.KeyVault, KeyVaultKeyId = ValidKeyId };

        var kind = EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named("Development"));

        Assert.Equal(KeyWrapProviderKind.KeyVault, kind);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ResolveProvider_NonDevelopmentWithValidKeyId_ReturnsKeyVault(string environmentName)
    {
        var options = new EncryptionOptions { KeyVaultKeyId = ValidKeyId };

        var kind = EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named(environmentName));

        Assert.Equal(KeyWrapProviderKind.KeyVault, kind);
    }

    [Fact]
    public void ResolveProvider_NonDevelopmentWithExplicitDevelopmentProvider_Throws()
    {
        var options = new EncryptionOptions
        {
            Provider = KeyWrapProviderKind.Development,
            DevelopmentKeyMaterial = TestEncryptionOptions.RandomMaterial(),
            KeyVaultKeyId = ValidKeyId,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named("Production")));

        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Message);
    }

    [Fact]
    public void ResolveProvider_NonDevelopmentWithDevelopmentKeyMaterial_ThrowsWithoutEchoingValue()
    {
        var material = Marker + new string('m', 40);
        var options = new EncryptionOptions { KeyVaultKeyId = ValidKeyId, DevelopmentKeyMaterial = material };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named("Production")));

        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialNotAllowed, exception.Message);
        Assert.DoesNotContain(Marker, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(material, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("short")]
    public void ResolveProvider_NonDevelopmentWithAnyNonEmptyDevelopmentKeyMaterial_Throws(string material)
    {
        var options = new EncryptionOptions { KeyVaultKeyId = ValidKeyId, DevelopmentKeyMaterial = material };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named("Production")));

        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialNotAllowed, exception.Message);
    }

    [Fact]
    public void ResolveProvider_NonDevelopmentWithoutKeyId_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(new EncryptionOptions(), HostEnvironments.Named("Production")));

        Assert.Equal(EncryptionMessageText.KeyVaultKeyIdInvalid, exception.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidKeyIds))]
    public void ResolveProvider_NonDevelopmentWithInvalidKeyId_ThrowsWithoutEchoingValue(string keyId)
    {
        var options = new EncryptionOptions { KeyVaultKeyId = keyId };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named("Production")));

        Assert.Equal(EncryptionMessageText.KeyVaultKeyIdInvalid, exception.Message);
        Assert.DoesNotContain(keyId, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, "Production")]
    [InlineData(-1, "Development")]
    public void ResolveProvider_UndefinedProviderValue_Throws(int value, string environmentName)
    {
        var options = new EncryptionOptions
        {
            Provider = (KeyWrapProviderKind)value,
            KeyVaultKeyId = ValidKeyId,
            DevelopmentKeyMaterial = environmentName == "Development" ? TestEncryptionOptions.RandomMaterial() : null,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EncryptionSettingsGuard.ResolveProvider(options, HostEnvironments.Named(environmentName)));

        Assert.Equal(EncryptionMessageText.UnknownProvider, exception.Message);
    }

    [Fact]
    public void TryResolveProvider_InvalidSettings_ReturnsFalseWithSameMessageAsResolve()
    {
        var options = new EncryptionOptions { Provider = KeyWrapProviderKind.Development };
        var environment = HostEnvironments.Named("Production");

        var resolved = EncryptionSettingsGuard.TryResolveProvider(options, environment, out _, out var error);
        var thrown = Assert.Throws<InvalidOperationException>(() => EncryptionSettingsGuard.ResolveProvider(options, environment));

        Assert.False(resolved);
        Assert.Equal(thrown.Message, error);
    }

    [Fact]
    public void TryResolveProvider_NonDevelopmentWithDevelopmentProvider_NeverReportsSuccess()
    {
        foreach (var environmentName in new[] { "Production", "Development2", string.Empty })
        {
            var options = new EncryptionOptions
            {
                Provider = KeyWrapProviderKind.Development,
                DevelopmentKeyMaterial = TestEncryptionOptions.RandomMaterial(),
            };

            var resolved = EncryptionSettingsGuard.TryResolveProvider(options, HostEnvironments.Named(environmentName), out _, out var error);

            Assert.False(resolved);
            Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, error);
        }
    }

    [Fact]
    public void EncryptionOptions_ToString_ContainsNoKeyMaterial()
    {
        var material = Marker + new string('m', 40);
        var options = new EncryptionOptions { DevelopmentKeyMaterial = material, KeyVaultKeyId = ValidKeyId };

        var text = options.ToString();

        Assert.DoesNotContain(material, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
    }
}
