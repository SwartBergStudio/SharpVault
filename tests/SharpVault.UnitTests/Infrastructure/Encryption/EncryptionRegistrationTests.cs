// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

public sealed class EncryptionRegistrationTests
{
    private const string ValidKeyId = "https://sharpvault-test.vault.azure.net/keys/sharpvault-kek";
    private const string AzuriteConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";

    private static WebApplicationBuilder CreateBuilder(string environment, params (string Key, string? Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));
        return builder;
    }

    [Fact]
    public void AddEncryption_Development_RegistersDevelopmentProviderAndSingletonEncryptor()
    {
        var builder = CreateBuilder("Development", ("Encryption:DevelopmentKeyMaterial", TestEncryptionOptions.RandomMaterial()));

        builder.AddEncryption();
        using var provider = builder.Services.BuildServiceProvider();

        Assert.IsType<DevelopmentKeyWrapProvider>(provider.GetRequiredService<IKeyWrapProvider>());
        Assert.Same(provider.GetRequiredService<IEnvelopeEncryptor>(), provider.GetRequiredService<IEnvelopeEncryptor>());
        Assert.IsType<EnvelopeEncryptor>(provider.GetRequiredService<IEnvelopeEncryptor>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddEncryption_Production_RegistersKeyVaultProvider()
    {
        var builder = CreateBuilder("Production", ("Encryption:KeyVaultKeyId", ValidKeyId));

        builder.AddEncryption();
        using var provider = builder.Services.BuildServiceProvider();
        var wrapProvider = provider.GetRequiredService<IKeyWrapProvider>();

        Assert.IsType<KeyVaultKeyWrapProvider>(wrapProvider);
        Assert.IsNotType<DevelopmentKeyWrapProvider>(wrapProvider);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddEncryption_ProductionWithDevelopmentProvider_ThrowsDuringRegistration()
    {
        var builder = CreateBuilder(
            "Production",
            ("Encryption:Provider", "Development"),
            ("Encryption:DevelopmentKeyMaterial", TestEncryptionOptions.RandomMaterial()));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddEncryption());

        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Message);
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(IKeyWrapProvider));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Dev")]
    [InlineData("")]
    public void AddEncryption_OtherEnvironmentWithDevelopmentProvider_Throws(string environment)
    {
        var builder = CreateBuilder(environment, ("Encryption:Provider", "Development"), ("Encryption:KeyVaultKeyId", ValidKeyId));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddEncryption());

        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Message);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Production", "7")]
    [InlineData("Development", "7")]
    [InlineData("Production", "-1")]
    [InlineData("Production", "2")]
    public void AddEncryption_ProviderConfiguredAsUndefinedNumber_Throws(string environment, string value)
    {
        var builder = CreateBuilder(
            environment,
            ("Encryption:Provider", value),
            ("Encryption:KeyVaultKeyId", ValidKeyId),
            ("Encryption:DevelopmentKeyMaterial", environment == "Development" ? TestEncryptionOptions.RandomMaterial() : null));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddEncryption());

        Assert.Equal(EncryptionMessageText.UnknownProvider, exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddEncryption_ProviderConfiguredAsNumberOneInProduction_Throws()
    {
        var builder = CreateBuilder("Production", ("Encryption:Provider", "1"), ("Encryption:KeyVaultKeyId", ValidKeyId));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddEncryption());

        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddEncryption_ProviderConfiguredInLowerCaseDevelopmentInProduction_Throws()
    {
        var builder = CreateBuilder("Production", ("Encryption:Provider", "development"), ("Encryption:KeyVaultKeyId", ValidKeyId));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddEncryption());

        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddEncryption_ProductionWithDevelopmentKeyMaterial_ThrowsWithoutEchoingIt()
    {
        var material = "SV-MARKER-registration-" + new string('z', 30);
        var builder = CreateBuilder("Production", ("Encryption:KeyVaultKeyId", ValidKeyId), ("Encryption:DevelopmentKeyMaterial", material));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddEncryption());

        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialNotAllowed, exception.Message);
        Assert.DoesNotContain(material, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddEncryption_Registered_AddsOptionsValidatorAndStartupCheck()
    {
        var builder = CreateBuilder("Development", ("Encryption:DevelopmentKeyMaterial", TestEncryptionOptions.RandomMaterial()));

        builder.AddEncryption();
        using var provider = builder.Services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<IValidateOptions<EncryptionOptions>>(), validator => validator is EncryptionOptionsValidator);
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is KeyWrapProviderStartupCheck);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void OptionsValidation_ProductionHostConfiguredAfterRegistrationWithDevelopmentProvider_FailsOnAccess()
    {
        var builder = CreateBuilder("Production", ("Encryption:KeyVaultKeyId", ValidKeyId));
        builder.AddEncryption();
        builder.Services.Configure<EncryptionOptions>(options => options.Provider = KeyWrapProviderKind.Development);
        using var provider = builder.Services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EncryptionOptions>>().Value);

        Assert.Contains(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Failures);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StartupCheckConstruction_ProductionWithDevelopmentProviderRegisteredDirectly_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(SharpVault.UnitTests.Support.HostEnvironments.Named("Production"));
        services.AddSingleton(TestEncryptionOptions.Development());
        services.AddSingleton<IKeyWrapProvider, DevelopmentKeyWrapProvider>();
        services.AddLogging();
        services.AddSingleton<KeyWrapProviderStartupCheck>();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<KeyWrapProviderStartupCheck>());

        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Message);
    }

    [Fact]
    public void AddInfrastructure_Development_ResolvesEnvelopeEncryptor()
    {
        var builder = CreateBuilder(
            "Development",
            ($"ConnectionStrings:{TableStorageConnection.Name}", AzuriteConnectionString),
            ("Encryption:DevelopmentKeyMaterial", TestEncryptionOptions.RandomMaterial()));

        builder.AddInfrastructure();
        using var provider = builder.Services.BuildServiceProvider();

        Assert.IsType<EnvelopeEncryptor>(provider.GetRequiredService<IEnvelopeEncryptor>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddInfrastructure_ProductionWithDevelopmentProvider_ThrowsDuringRegistration()
    {
        var builder = CreateBuilder(
            "Production",
            ("ConnectionStrings:" + TableStorageConnection.Name, "https://example.table.core.windows.net/"),
            ("Encryption:Provider", "Development"),
            ("Encryption:DevelopmentKeyMaterial", TestEncryptionOptions.RandomMaterial()));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddInfrastructure());

        Assert.Equal(EncryptionMessageText.DevelopmentProviderNotAllowed, exception.Message);
    }
}
