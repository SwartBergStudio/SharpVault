// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(ProductionTestGroup))]
[Trait("Category", "Security")]
public sealed class ProductionEncryptionStartupTests(ProductionApiTestFactory factory)
{
    public static TheoryData<string> InvalidKeyVaultKeyIds =>
    [
        "https://sharpvaultfoundationtest.vault.azure.net/keys/sharpvault-kek/0123456789abcdef0123456789abcdef",
        "http://sharpvaultfoundationtest.vault.azure.net/keys/sharpvault-kek",
        "https://sharpvaultfoundationtest.vault.azure.net/keys/sharpvault-kek?sig=abc123",
        "https://user:hunter2@sharpvaultfoundationtest.vault.azure.net/keys/sharpvault-kek",
        "https://sharpvaultfoundationtest.vault.azure.net/secrets/sharpvault-kek",
    ];

    [Fact]
    public void Startup_Production_ResolvesKeyVaultKeyWrapProvider()
    {
        using var client = factory.CreateUnauthenticatedClient();

        var provider = factory.Services.GetRequiredService<IKeyWrapProvider>();

        Assert.IsType<KeyVaultKeyWrapProvider>(provider);
        Assert.IsNotType<DevelopmentKeyWrapProvider>(provider);
        Assert.IsType<EnvelopeEncryptor>(factory.Services.GetRequiredService<IEnvelopeEncryptor>());
    }

    [Fact]
    public void Startup_Production_LogsKeyVaultProviderInUse()
    {
        factory.Logs.Clear();

        using var derived = factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();

        var entry = Assert.Single(factory.Logs.Entries, entry => entry.Message == EncryptionMessageText.KeyVaultInUse);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message == EncryptionMessageText.DevelopmentWarning);
    }

    [Fact]
    public void Startup_Production_ReachesNoKeyVaultHostDuringStartup()
    {
        factory.Logs.Clear();

        using var derived = factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();

        Assert.DoesNotContain(
            factory.Logs.Entries,
            entry => entry.Category.StartsWith("Azure", StringComparison.Ordinal) && entry.Message.Contains("vault.azure.net", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Startup_ProductionWithDevelopmentProvider_FailsWithoutEchoingKeyMaterial()
    {
        var material = "SV-MARKER-prod-provider-" + RandomNumberGenerator.GetHexString(16, true);
        factory.Logs.Clear();
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Encryption:Provider", "Development");
            builder.UseSetting("Encryption:DevelopmentKeyMaterial", material);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, invalid.Message);
        Assert.DoesNotContain(material, exception.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText(material));
    }

    [Fact]
    public void Startup_ProductionWithDevelopmentProviderAndNoKeyMaterial_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:Provider", "Development"));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, invalid.Message);
    }

    [Fact]
    public void Startup_ProductionWithDevelopmentKeyMaterial_FailsWithoutEchoingIt()
    {
        var material = "SV-MARKER-" + RandomNumberGenerator.GetHexString(30, true);
        Assert.Equal(40, material.Length);
        factory.Logs.Clear();
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:DevelopmentKeyMaterial", material));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialNotAllowed, invalid.Message);
        Assert.DoesNotContain(material, exception.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText(material));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void Startup_NonDevelopmentEnvironmentWithDevelopmentProvider_Fails(string environment)
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Encryption:Provider", "Development");
            builder.UseSetting("Encryption:DevelopmentKeyMaterial", RandomNumberGenerator.GetHexString(48, true));
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, invalid.Message);
    }

    [Fact]
    public void Startup_StagingWithDevelopmentProvider_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("Encryption:Provider", "Development");
        });

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Startup_ProductionWithoutKeyVaultKeyId_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:KeyVaultKeyId", null));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.KeyVaultKeyIdInvalid, invalid.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidKeyVaultKeyIds))]
    public void Startup_ProductionWithInvalidKeyVaultKeyId_FailsWithoutEchoingIt(string keyId)
    {
        factory.Logs.Clear();
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:KeyVaultKeyId", keyId));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.KeyVaultKeyIdInvalid, invalid.Message);
        Assert.DoesNotContain(keyId, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", exception.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText(keyId));
        Assert.False(factory.Logs.ContainsText("hunter2"));
    }

    [Fact]
    public void Startup_ProductionWithProviderAsNumberOne_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:Provider", "1"));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, invalid.Message);
    }

    [Fact]
    public void Startup_ProductionWithProviderAsUndefinedNumber_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:Provider", "7"));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.UnknownProvider, invalid.Message);
    }

    [Fact]
    public void Startup_ProductionWithLowerCaseDevelopmentProvider_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:Provider", "development"));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, invalid.Message);
    }
}
