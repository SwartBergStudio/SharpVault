// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Azure;
using Azure.Core.Diagnostics;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption.KeyVault;

public sealed class KeyVaultSdkPipelineTests : IDisposable
{
    private const string Algorithm = "RSA-OAEP-256";
    private const string KeyPath = "/keys/" + FakeKeyVault.KeyName;
    private const string PlaintextMarker = "SV-PLAINTEXT-91bc";
    private const string EvilKeyId = "https://evil.example/keys/" + FakeKeyVault.KeyName + "/0123456789abcdef0123456789abcdef";

    private readonly FakeKeyVaultHttpHandler _handler = new();
    private readonly KeyVaultCryptographyClientFactory _factory;
    private readonly CollectingLogger<EnvelopeEncryptor> _logger = new();

    public KeyVaultSdkPipelineTests()
    {
        _factory = _handler.CreateFactory();
    }

    [Fact]
    public async Task CreateDataKeyAsync_ReadableKeyTwice_ReadsKeyOnceAndWrapsLocally()
    {
        var encryptor = CreateEncryptor(_factory);

        using var first = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);
        using var second = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);

        var authorized = _handler.AuthorizedRequests.ToList();
        Assert.Single(authorized, request => request.Method == "GET" && request.Path == KeyPath);
        Assert.DoesNotContain(authorized, request => request.Method == "POST");
        Assert.Equal(_handler.CurrentKeyId, first.WrappedKey.KeyId);
        Assert.Equal(_handler.CurrentKeyId, second.WrappedKey.KeyId);
        Assert.Equal(Algorithm, first.WrappedKey.Algorithm);
        Assert.Equal(256, first.WrappedKey.Value.Length);
    }

    [Fact]
    public async Task CreateDataKeyAsync_KeyReadForbidden_SendsOneWrapKeyRequestPerCall()
    {
        _handler.AllowKeyRead = false;
        var encryptor = CreateEncryptor(_factory);

        using var first = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);
        using var second = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);

        var wraps = _handler.AuthorizedRequests.Where(request => request.Method == "POST").ToList();
        Assert.Equal(2, wraps.Count);
        Assert.All(wraps, request => Assert.Equal(KeyPath + "/wrapKey", request.Path));
        Assert.Equal(_handler.CurrentKeyId, first.WrappedKey.KeyId);
    }

    [Fact]
    public async Task OpenDataKeyAsync_Twice_ReadsKeyOnceAndSendsOneUnwrapKeyRequestPerCall()
    {
        var encryptor = CreateEncryptor(_factory);
        WrappedDataKey wrapped;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
        }

        var versionedPath = $"{KeyPath}/{_handler.CurrentVersion}";

        using var first = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);
        using var second = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);

        var authorized = _handler.AuthorizedRequests.ToList();
        Assert.Equal(2, authorized.Count(request => request.Method == "POST" && request.Path == versionedPath + "/unwrapKey"));
        Assert.Single(authorized, request => request.Method == "GET" && request.Path == versionedPath);
        Assert.DoesNotContain(authorized, request => request.Path.EndsWith("/wrapKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task KeyVaultRequests_WrapAndUnwrap_UseRsaOaep256OnTheWire()
    {
        _handler.AllowKeyRead = false;
        var encryptor = CreateEncryptor(_factory);
        WrappedDataKey wrapped;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
        }

        using var opened = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);

        var operations = _handler.AuthorizedRequests.Where(request => request.Method == "POST").ToList();
        Assert.Contains(operations, request => request.Path.EndsWith("/wrapKey", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(operations, request => request.Path.EndsWith("/unwrapKey", StringComparison.OrdinalIgnoreCase));
        Assert.All(operations, request => Assert.Equal(Algorithm, request.Algorithm));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task KeyVaultRequests_AnyOperation_GoOnlyToConfiguredVaultHost()
    {
        var encryptor = CreateEncryptor(_factory);
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            using var opened = await encryptor.OpenDataKeyAsync(created.WrappedKey, TestContext.Current.CancellationToken);
            await encryptor.RewrapDataKeyAsync(created.WrappedKey, TestContext.Current.CancellationToken);
        }

        var requests = _handler.Requests;
        Assert.NotEmpty(requests);
        Assert.All(requests, request =>
        {
            Assert.Equal(FakeKeyVaultHttpHandler.VaultHost, request.Host);
            Assert.Equal(Uri.UriSchemeHttps, request.Scheme);
            Assert.StartsWith("/keys/" + FakeKeyVault.KeyName, request.Path, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_StoredKeyIdOnOtherHost_SendsNoRequestAndNoToken()
    {
        var provider = CreateProvider(_factory);
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var requestsBefore = _handler.Requests.Count;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { KeyId = EvilKeyId }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
        Assert.Equal(requestsBefore, _handler.Requests.Count);
        Assert.DoesNotContain(_handler.Requests, request => request.Host != FakeKeyVaultHttpHandler.VaultHost);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_StoredKeyIdWithHttpScheme_SendsNoRequest()
    {
        var provider = CreateProvider(_factory);
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var requestsBefore = _handler.Requests.Count;
        var insecure = wrapped with { KeyId = wrapped.KeyId.Replace("https://", "http://", StringComparison.Ordinal) };

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(() => provider.UnwrapAsync(insecure, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
        Assert.Equal(requestsBefore, _handler.Requests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_TamperedWrappedValueOverTheWire_ThrowsAuthenticationFailed()
    {
        var provider = CreateProvider(_factory);
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var tampered = wrapped.Value.ToArray();
        tampered[10] ^= 0x01;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { Value = tampered }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
        Assert.Null(exception.InnerException);
        Assert.Contains(_handler.AuthorizedRequests, request => request.Path.EndsWith("/unwrapKey", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(403)]
    [InlineData(404)]
    public async Task UnwrapAsync_KeyVaultAnswersWithOtherStatusOverTheWire_PropagatesRequestFailedExceptionWithoutKeyMaterial(int status)
    {
        var provider = CreateProvider(_factory);
        var dataKey = NewDataKey();
        var dataKeyCopy = dataKey.ToArray();
        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        _handler.UnwrapStatusOverride = status;

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() => provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.Status);
        KeyMaterialLeakAssert.NotLeaked(exception.ToString(), dataKeyCopy, wrapped.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WrapAsync_RemoteWrapAfterRotation_UsesNewVersionWithoutRestart()
    {
        _handler.AllowKeyRead = false;
        var provider = CreateProvider(_factory);
        var before = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);

        var newVersion = _handler.Rotate();
        var after = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);

        Assert.NotEqual(before.KeyId, after.KeyId);
        Assert.EndsWith(newVersion, after.KeyId, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Rotation_AfterRestart_NewWrapsUseNewVersionAndOldWrappedKeysStillUnwrap()
    {
        var encryptorBefore = CreateEncryptor(_factory);
        var context = EncryptionContexts.Random();
        WrappedDataKey oldWrapped;
        byte[] oldEnvelope;
        using (var created = await encryptorBefore.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            oldWrapped = created.WrappedKey;
            oldEnvelope = created.Encrypt(context, PlaintextMarker);
        }

        var newVersion = _handler.Rotate();
        var encryptorAfter = CreateEncryptor(_handler.CreateFactory());

        WrappedDataKey newWrapped;
        using (var created = await encryptorAfter.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            newWrapped = created.WrappedKey;
        }

        Assert.EndsWith(newVersion, newWrapped.KeyId, StringComparison.Ordinal);
        Assert.NotEqual(oldWrapped.KeyId, newWrapped.KeyId);
        using var openedOld = await encryptorAfter.OpenDataKeyAsync(oldWrapped, TestContext.Current.CancellationToken);
        Assert.Equal(PlaintextMarker, openedOld.Decrypt(context, oldEnvelope));
        using var openedNew = await encryptorAfter.OpenDataKeyAsync(newWrapped, TestContext.Current.CancellationToken);
        Assert.NotNull(openedNew);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task KeyVaultRequests_VerboseSdkLogging_NeverContainDataKeyWrappedKeyOrToken()
    {
        var messages = new ConcurrentQueue<string>();
        using var listener = new AzureEventSourceListener((_, message) => messages.Enqueue(message), EventLevel.Verbose);
        _handler.AllowKeyRead = false;
        var encryptor = CreateEncryptor(_factory);
        WrappedDataKey wrapped;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
        }

        using var opened = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);
        var tampered = wrapped.Value.ToArray();
        tampered[0] ^= 0x01;
        await Assert.ThrowsAsync<SecretDecryptionException>(() => CreateProvider(_factory).UnwrapAsync(wrapped with { Value = tampered }, TestContext.Current.CancellationToken));

        var logged = string.Join('\n', messages);
        Assert.Contains(FakeKeyVaultHttpHandler.VaultHost, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(StaticTokenCredential.Token, logged, StringComparison.Ordinal);
        var values = _handler.Requests
            .SelectMany(request => new[] { request.RequestValue, request.ResponseValue })
            .OfType<string>()
            .Where(value => value.Length > 0)
            .ToList();
        Assert.NotEmpty(values);
        foreach (var value in values)
        {
            Assert.DoesNotContain(value, logged, StringComparison.Ordinal);
            KeyMaterialLeakAssert.NotLeaked(logged, KeyMaterialLeakAssert.FromBase64Url(value));
        }

        KeyMaterialLeakAssert.NotLeaked(logged, wrapped.Value, tampered);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task KeyVaultRequests_ContentLoggingEnabled_ListenerSeesBodiesSoTheLeakCheckIsNotVacuous()
    {
        var messages = new ConcurrentQueue<string>();
        using var listener = new AzureEventSourceListener((_, message) => messages.Enqueue(message), EventLevel.Verbose);
        _handler.AllowKeyRead = false;
        var encryptor = CreateEncryptor(_handler.CreateFactory(contentLogging: true));

        using var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);

        var requestValue = Assert.Single(_handler.AuthorizedRequests, request => request.Path.EndsWith("/wrapKey", StringComparison.OrdinalIgnoreCase)).RequestValue;
        Assert.Contains(requestValue!, string.Join('\n', messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoundTrip_RealSdk_DecryptsEnvelope()
    {
        var encryptor = CreateEncryptor(_factory);
        var context = EncryptionContexts.Random();
        WrappedDataKey wrapped;
        byte[] envelope;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
            envelope = created.Encrypt(context, PlaintextMarker);
        }

        using var opened = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(PlaintextMarker, opened.Decrypt(context, envelope));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RoundTrip_RealSdkWithRemoteWrap_DecryptsEnvelope()
    {
        _handler.AllowKeyRead = false;
        var encryptor = CreateEncryptor(_factory);
        var context = EncryptionContexts.Random();
        WrappedDataKey wrapped;
        byte[] envelope;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
            envelope = created.Encrypt(context, PlaintextMarker);
        }

        using var opened = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(PlaintextMarker, opened.Decrypt(context, envelope));
    }

    public void Dispose() => _handler.Dispose();

    private static byte[] NewDataKey() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(EnvelopeFormat.DataKeySize);

    private static KeyVaultKeyWrapProvider CreateProvider(KeyVaultCryptographyClientFactory factory) =>
        new(factory, TestEncryptionOptions.KeyVault(FakeKeyVault.VersionlessKeyId));

    private EnvelopeEncryptor CreateEncryptor(KeyVaultCryptographyClientFactory factory) =>
        new(CreateProvider(factory), _logger);
}
