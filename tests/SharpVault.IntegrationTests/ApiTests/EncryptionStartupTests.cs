// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
public sealed class EncryptionStartupTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string PlaintextMarker = "SV-PLAINTEXT-5c1e";
    private const string KeyVaultKeyId = "https://sharpvaultfoundationtest.vault.azure.net/keys/sharpvault-kek";

    [Fact]
    public void Startup_Development_ResolvesDevelopmentKeyWrapProvider()
    {
        _ = Factory.Server;

        var provider = Factory.Services.GetRequiredService<IKeyWrapProvider>();

        Assert.IsType<DevelopmentKeyWrapProvider>(provider);
    }

    [Fact]
    public void Startup_Development_ResolvesEnvelopeEncryptorAsSingleton()
    {
        _ = Factory.Server;

        var first = Factory.Services.GetRequiredService<IEnvelopeEncryptor>();
        using var scope = Factory.Services.CreateScope();
        var second = scope.ServiceProvider.GetRequiredService<IEnvelopeEncryptor>();

        Assert.IsType<EnvelopeEncryptor>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task EnvelopeEncryptor_DevelopmentHostRoundTrip_MakesNoStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = Factory.Server;
        StorageRequests.Clear();
        var encryptor = Factory.Services.GetRequiredService<IEnvelopeEncryptor>();
        var context = new EncryptionContext("secret.value", Guid.NewGuid(), Guid.NewGuid());

        using var session = await encryptor.CreateDataKeyAsync(cancellationToken);
        var envelope = session.Encrypt(context, PlaintextMarker);
        using var opened = await encryptor.OpenDataKeyAsync(session.WrappedKey, cancellationToken);
        var decrypted = opened.Decrypt(context, envelope);

        Assert.Equal(PlaintextMarker, decrypted);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task EnvelopeEncryptor_DevelopmentHostRoundTrip_LogsNoPlaintextKeyMaterialOrEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = Factory.Server;
        Logs.Clear();
        var encryptor = Factory.Services.GetRequiredService<IEnvelopeEncryptor>();
        var context = new EncryptionContext("secret.value", Guid.NewGuid(), Guid.NewGuid());

        using var session = await encryptor.CreateDataKeyAsync(cancellationToken);
        var envelope = session.Encrypt(context, PlaintextMarker);
        using var opened = await encryptor.OpenDataKeyAsync(session.WrappedKey, cancellationToken);
        opened.Decrypt(context, envelope);
        using var rewrapped = await encryptor.OpenDataKeyAsync(await encryptor.RewrapDataKeyAsync(session.WrappedKey, cancellationToken), cancellationToken);
        rewrapped.ReEncrypt(context, context with { OwnerId = Guid.NewGuid() }, envelope);
        await Client.GetAsync("/alive", cancellationToken);

        AssertLogsDoNotContain(
            PlaintextMarker,
            Factory.DevelopmentKeyMaterial,
            Convert.ToBase64String(envelope),
            Convert.ToHexString(envelope),
            Convert.ToHexStringLower(envelope),
            Convert.ToBase64String(session.WrappedKey.Value),
            Convert.ToHexString(session.WrappedKey.Value),
            Convert.ToHexStringLower(session.WrappedKey.Value));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task EnvelopeEncryptor_DevelopmentHostTamperedEnvelope_LogsWarningWithoutData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = Factory.Server;
        var encryptor = Factory.Services.GetRequiredService<IEnvelopeEncryptor>();
        var itemId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var context = new EncryptionContext("secret.value", itemId, ownerId);
        using var session = await encryptor.CreateDataKeyAsync(cancellationToken);
        var envelope = session.Encrypt(context, PlaintextMarker);
        var tampered = (byte[])envelope.Clone();
        tampered[^1] ^= 0x01;
        Logs.Clear();

        var exception = Assert.Throws<SecretDecryptionException>(() => session.Decrypt(context, tampered));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
        var warning = Assert.Single(Logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(itemId.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains("secret.value", warning.Message, StringComparison.Ordinal);
        AssertLogsDoNotContain(
            PlaintextMarker,
            Factory.DevelopmentKeyMaterial,
            ownerId.ToString(),
            Convert.ToBase64String(envelope),
            Convert.ToHexString(tampered),
            Convert.ToBase64String(tampered),
            Convert.ToBase64String(session.WrappedKey.Value));
        Assert.DoesNotContain(PlaintextMarker, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_DevelopmentHost_LogsDevelopmentProviderWarning()
    {
        Logs.Clear();

        using var derived = Factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();

        var entry = Assert.Single(Logs.Entries, entry => entry.Message == EncryptionMessageText.DevelopmentWarning);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Startup_DevelopmentHost_LogsNoKeyMaterial()
    {
        Logs.Clear();

        using var derived = Factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();

        AssertLogsDoNotContain(Factory.DevelopmentKeyMaterial);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(null)]
    [InlineData("")]
    public void Startup_DevelopmentWithoutKeyMaterial_FailsWithoutEchoingConfiguration(string? material)
    {
        Logs.Clear();
        using var derived = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Encryption:DevelopmentKeyMaterial", material));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialMissing, invalid.Message);
        Assert.DoesNotContain(Factory.DevelopmentKeyMaterial, exception.ToString(), StringComparison.Ordinal);
        AssertLogsDoNotContain(Factory.DevelopmentKeyMaterial);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(false)]
    [InlineData(true)]
    public void Startup_DevelopmentWithShortKeyMaterial_FailsWithoutEchoingIt(bool padded)
    {
        var marker = "SV-MARKER-short-" + RandomNumberGenerator.GetHexString(15, true);
        Assert.Equal(31, marker.Length);
        var material = padded ? "  " + marker + "  " : marker;
        Logs.Clear();
        using var derived = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Encryption:DevelopmentKeyMaterial", material));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.DevelopmentKeyMaterialMissing, invalid.Message);
        Assert.DoesNotContain(marker, exception.ToString(), StringComparison.Ordinal);
        AssertLogsDoNotContain(marker);
    }

    [Fact]
    public void Startup_DevelopmentWithExplicitKeyVaultProvider_ResolvesKeyVaultKeyWrapProvider()
    {
        using var derived = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Encryption:Provider", "KeyVault");
            builder.UseSetting("Encryption:KeyVaultKeyId", KeyVaultKeyId);
            builder.UseSetting("Encryption:DevelopmentKeyMaterial", null);
        });
        using var client = derived.CreateClient();

        var provider = derived.Services.GetRequiredService<IKeyWrapProvider>();

        Assert.IsType<KeyVaultKeyWrapProvider>(provider);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Startup_DevelopmentWithExplicitKeyVaultProviderAndNoKeyId_Fails()
    {
        using var derived = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Encryption:Provider", "KeyVault");
            builder.UseSetting("Encryption:DevelopmentKeyMaterial", null);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.KeyVaultKeyIdInvalid, invalid.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Startup_DevelopmentWithUndefinedProviderNumber_Fails()
    {
        using var derived = Factory.WithWebHostBuilder(builder => builder.UseSetting("Encryption:Provider", "7"));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EncryptionMessageText.UnknownProvider, invalid.Message);
    }
}
