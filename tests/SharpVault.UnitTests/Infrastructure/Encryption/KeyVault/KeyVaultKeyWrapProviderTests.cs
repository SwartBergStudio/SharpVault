// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption.KeyVault;

public sealed class KeyVaultKeyWrapProviderTests : IDisposable
{
    private const string Algorithm = "RSA-OAEP-256";
    private const string KeyIdPrefix = FakeKeyVault.VaultUri + "/keys/" + FakeKeyVault.KeyName + "/";
    private const string SomeVersion = "0123456789abcdef0123456789abcdef";
    private const string PlaintextMarker = "SV-PLAINTEXT-7d2a";
    private const string EvilKeyId = "https://evil.example/keys/sharpvault-kek/" + SomeVersion;
    private const int RsaWrappedLength = 256;
    private const int DataKeyLength = 32;
    private const int SessionOperations = 100;

    private readonly FakeKeyVault _vault = new();
    private readonly CollectingLogger<EnvelopeEncryptor> _logger = new();

    public static TheoryData<int> NotDataKeyLengths => new() { 0, 31, 33 };

    public static TheoryData<string> UnexpectedReturnedKeyIds => new()
    {
        "https://sharpvault-test.vault.azure.net/keys/other-key/" + SomeVersion,
        "https://other.vault.azure.net/keys/sharpvault-kek/" + SomeVersion,
        "https://sharpvault-test.vault.azure.net.evil.example/keys/sharpvault-kek/" + SomeVersion,
        "http://sharpvault-test.vault.azure.net/keys/sharpvault-kek/" + SomeVersion,
        "https://sharpvault-test.vault.azure.net/keys/sharpvault-kek/" + SomeVersion + "?sig=abc",
        FakeKeyVault.VersionlessKeyId,
        string.Empty,
    };

    public static TheoryData<int> UnexpectedWrappedLengths => new() { 0, 255, 257, 513 };

    public static TheoryData<int> AllowedWrappedLengths => new() { 256, 384, 512 };

    public static TheoryData<string> WrongAlgorithms => new()
    {
        "RSA1_5",
        "RSA-OAEP",
        "rsa-oaep-256",
        "development-A256GCM",
    };

    public static TheoryData<string> ForeignKeyIds => new()
    {
        "https://other.vault.azure.net/keys/sharpvault-kek/" + SomeVersion,
        EvilKeyId,
        "https://sharpvault-test.vault.azure.net/keys/other-key/" + SomeVersion,
        FakeKeyVault.VersionlessKeyId,
        "http://sharpvault-test.vault.azure.net/keys/sharpvault-kek/" + SomeVersion,
        "https://user:pw@sharpvault-test.vault.azure.net/keys/sharpvault-kek/" + SomeVersion,
        "development:0123456789abcdef",
        "https://sharpvault-test.vault.azure.net/keys/sharpvault-kek/" + SomeVersion + "?x=1",
    };

    public static TheoryData<int> NotRsaSizedWrappedLengths => new() { 0, 255, 513 };

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("http://sharpvault-test.vault.azure.net/keys/sharpvault-kek")]
    [InlineData("https://sharpvault-test.vault.azure.net/secrets/sharpvault-kek")]
    [InlineData("https://user:pw@sharpvault-test.vault.azure.net/keys/sharpvault-kek")]
    [InlineData("   ")]
    public void Constructor_InvalidConfiguredKeyId_Throws(string keyId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new KeyVaultKeyWrapProvider(_vault, TestEncryptionOptions.KeyVault(keyId)));

        Assert.Equal(EncryptionMessageText.KeyVaultKeyIdInvalid, exception.Message);
        Assert.Equal(0, _vault.WrapCount + _vault.UnwrapCount);
    }

    [Fact]
    public void Constructor_ValidKeyId_RequestsOnlyConfiguredClientAndMakesNoCalls()
    {
        _ = CreateProvider();

        Assert.Equal([FakeKeyVault.VersionlessKeyId], _vault.RequestedKeyIds);
        Assert.Equal(0, _vault.WrapCount);
        Assert.Equal(0, _vault.UnwrapCount);
    }

    [Fact]
    public async Task WrapAsync_ValidDataKey_ReturnsVersionedKeyIdRsaOaep256AndWrappedValue()
    {
        var provider = CreateProvider();

        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);

        Assert.Equal(_vault.CurrentKeyId, wrapped.KeyId);
        Assert.Equal(Algorithm, wrapped.Algorithm);
        Assert.Equal(RsaWrappedLength, wrapped.Value.Length);
        Assert.Equal([Algorithm], _vault.WrapAlgorithms);
    }

    [Theory]
    [MemberData(nameof(NotDataKeyLengths))]
    public async Task WrapAsync_DataKeyNot32Bytes_ThrowsWithoutCallingKeyVault(int length)
    {
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => provider.WrapAsync(new byte[length], TestContext.Current.CancellationToken));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.InvalidDataKeyLength, "dataKey"), exception.Message);
        Assert.Equal(0, _vault.WrapCount);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(UnexpectedReturnedKeyIds))]
    public async Task WrapAsync_KeyVaultReturnsUnexpectedKeyId_ThrowsUnexpectedKey(string returnedKeyId)
    {
        var provider = CreateProvider();
        _vault.WrapKeyIdOverride = returnedKeyId;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken));

        Assert.Equal(EncryptionMessageText.KeyVaultReturnedUnexpectedKey, exception.Message);
    }

    [Theory]
    [MemberData(nameof(UnexpectedWrappedLengths))]
    public async Task WrapAsync_KeyVaultReturnsUnexpectedWrappedLength_ThrowsUnexpectedKey(int length)
    {
        var provider = CreateProvider();
        _vault.WrapLengthOverride = length;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken));

        Assert.Equal(EncryptionMessageText.KeyVaultReturnedUnexpectedKey, exception.Message);
    }

    [Theory]
    [MemberData(nameof(AllowedWrappedLengths))]
    public async Task WrapAsync_RsaSizedWrappedLength_IsAccepted(int length)
    {
        var provider = CreateProvider();
        _vault.WrapLengthOverride = length;

        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);

        Assert.Equal(length, wrapped.Value.Length);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WrapAsync_AfterKeyRotation_UsesNewVersion()
    {
        var provider = CreateProvider();
        var before = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);

        var newVersion = _vault.Rotate();
        var after = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);

        Assert.Equal(KeyIdPrefix + newVersion, after.KeyId);
        Assert.NotEqual(before.KeyId, after.KeyId);
    }

    [Fact]
    public async Task WrapAsync_WithToken_PassesTokenToClient()
    {
        var provider = CreateProvider();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        await provider.WrapAsync(NewDataKey(), source.Token);

        Assert.Equal(source.Token, Assert.Single(_vault.WrapTokens));
    }

    [Fact]
    public async Task UnwrapAsync_WrappedByProvider_ReturnsOriginalDataKey()
    {
        var provider = CreateProvider();
        var dataKey = NewDataKey();
        var original = dataKey.ToArray();
        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);

        var unwrapped = await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(original, unwrapped);
        Assert.Equal([Algorithm], _vault.UnwrapAlgorithms);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_AfterKeyRotation_UnwrapsOldVersionWithItsOwnClient()
    {
        var provider = CreateProvider();
        var original = NewDataKey();
        var copy = original.ToArray();
        var wrapped = await provider.WrapAsync(original, TestContext.Current.CancellationToken);
        _vault.Rotate();

        var unwrapped = await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(copy, unwrapped);
        Assert.Equal(wrapped.KeyId, _vault.RequestedKeyIds[^1]);
        Assert.NotEqual(_vault.CurrentKeyId, wrapped.KeyId);
    }

    [Fact]
    public async Task UnwrapAsync_KeyIdDifferingOnlyInCase_IsAccepted()
    {
        var provider = CreateProvider();
        var original = NewDataKey();
        var copy = original.ToArray();
        var wrapped = await provider.WrapAsync(original, TestContext.Current.CancellationToken);
        var upper = wrapped with { KeyId = $"HTTPS://SHARPVAULT-TEST.VAULT.AZURE.NET/KEYS/SHARPVAULT-KEK/{_vault.CurrentVersion}" };

        var unwrapped = await provider.UnwrapAsync(upper, TestContext.Current.CancellationToken);

        Assert.Equal(copy, unwrapped);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(WrongAlgorithms))]
    public async Task UnwrapAsync_AlgorithmNotRsaOaep256_ThrowsKeyMismatchWithoutCallingKeyVault(string algorithm)
    {
        var provider = CreateProvider();
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var requestedBefore = _vault.RequestedKeyIds.Count;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { Algorithm = algorithm }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
        Assert.Equal(0, _vault.UnwrapCount);
        Assert.Equal(requestedBefore, _vault.RequestedKeyIds.Count);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(ForeignKeyIds))]
    public async Task UnwrapAsync_KeyIdNotAVersionOfConfiguredKey_ThrowsKeyMismatchWithoutCallingKeyVault(string keyId)
    {
        var provider = CreateProvider();
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var requestedBefore = _vault.RequestedKeyIds.Count;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { KeyId = keyId }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
        Assert.Equal(0, _vault.UnwrapCount);
        Assert.Equal(requestedBefore, _vault.RequestedKeyIds.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_DevelopmentWrappedKey_ThrowsKeyMismatchWithoutCallingKeyVault()
    {
        using var development = new DevelopmentKeyWrapProvider(HostEnvironments.Named("Development"), TestEncryptionOptions.Development());
        var developmentWrapped = await development.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(() => provider.UnwrapAsync(developmentWrapped, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
        Assert.Equal(0, _vault.UnwrapCount);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(NotRsaSizedWrappedLengths))]
    public async Task UnwrapAsync_WrappedValueLengthNotRsaSize_ThrowsMalformedWithoutCallingKeyVault(int length)
    {
        var provider = CreateProvider();
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var requestedBefore = _vault.RequestedKeyIds.Count;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { Value = new byte[length] }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.MalformedWrappedKey, exception.Reason);
        Assert.Equal(0, _vault.UnwrapCount);
        Assert.Equal(requestedBefore, _vault.RequestedKeyIds.Count);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public async Task UnwrapAsync_TamperedWrappedValue_ThrowsAuthenticationFailed(int index)
    {
        var provider = CreateProvider();
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        var tampered = wrapped.Value.ToArray();
        tampered[index] ^= 0x01;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { Value = tampered }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
        Assert.Null(exception.InnerException);
        Assert.Equal(EncryptionMessageText.SecretDecryption, exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_WrappedUnderDifferentKeyVersion_ThrowsAuthenticationFailed()
    {
        var provider = CreateProvider();
        var oldWrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        _vault.Rotate();
        var newWrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(newWrapped with { KeyId = oldWrapped.KeyId }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_KeyVaultReportsBadRequest_ThrowsAuthenticationFailedWithoutInnerException()
    {
        var provider = CreateProvider();
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        _vault.UnwrapFailureStatus = 400;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(() => provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(403)]
    [InlineData(503)]
    public async Task UnwrapAsync_KeyVaultFailsWithOtherStatus_PropagatesRequestFailedException(int status)
    {
        var provider = CreateProvider();
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        _vault.UnwrapFailureStatus = status;

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() => provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.Status);
    }

    [Fact]
    public async Task UnwrapAsync_WithToken_PassesTokenToClient()
    {
        var provider = CreateProvider();
        var wrapped = await provider.WrapAsync(NewDataKey(), TestContext.Current.CancellationToken);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        await provider.UnwrapAsync(wrapped, source.Token);

        Assert.Equal(source.Token, Assert.Single(_vault.UnwrapTokens));
    }

    [Fact]
    public async Task CreateDataKeyAsync_KeyVault_CallsWrapOnceAndUnwrapNever()
    {
        var encryptor = CreateEncryptor();

        using var session = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _vault.WrapCount);
        Assert.Equal(0, _vault.UnwrapCount);
        Assert.Equal(_vault.CurrentKeyId, session.WrappedKey.KeyId);
    }

    [Fact]
    public async Task OpenDataKeyAsync_KeyVault_CallsUnwrapOnceAndWrapNever()
    {
        var encryptor = CreateEncryptor();
        WrappedDataKey wrapped;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
        }

        var wrapsBefore = _vault.WrapCount;

        using var opened = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(wrapsBefore, _vault.WrapCount);
        Assert.Equal(1, _vault.UnwrapCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RewrapDataKeyAsync_AfterRotation_WrapsWithNewVersionAndOpensToSameKey()
    {
        var encryptor = CreateEncryptor();
        var context = EncryptionContexts.Random();
        WrappedDataKey oldWrapped;
        byte[] envelope;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            oldWrapped = created.WrappedKey;
            envelope = created.Encrypt(context, PlaintextMarker);
        }

        var newVersion = _vault.Rotate();

        var rewrapped = await encryptor.RewrapDataKeyAsync(oldWrapped, TestContext.Current.CancellationToken);

        Assert.Equal(KeyIdPrefix + newVersion, rewrapped.KeyId);
        Assert.NotEqual(oldWrapped.KeyId, rewrapped.KeyId);
        using var opened = await encryptor.OpenDataKeyAsync(rewrapped, TestContext.Current.CancellationToken);
        Assert.Equal(PlaintextMarker, opened.Decrypt(context, envelope));
    }

    [Fact]
    public async Task Session_EncryptDecryptAndReEncryptHundredTimes_MakesNoKeyVaultCalls()
    {
        var encryptor = CreateEncryptor();
        using var session = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);
        var wrapsBefore = _vault.WrapCount;
        var unwrapsBefore = _vault.UnwrapCount;
        var requestsBefore = _vault.RequestedKeyIds.Count;

        for (var index = 0; index < SessionOperations; index++)
        {
            var source = EncryptionContexts.Random();
            var target = EncryptionContexts.Random();
            var envelope = session.Encrypt(source, PlaintextMarker);
            Assert.Equal(PlaintextMarker, session.Decrypt(source, envelope));
            var moved = session.ReEncrypt(source, target, envelope);
            Assert.Equal(PlaintextMarker, session.Decrypt(target, moved));
        }

        Assert.Equal(wrapsBefore, _vault.WrapCount);
        Assert.Equal(unwrapsBefore, _vault.UnwrapCount);
        Assert.Equal(requestsBefore, _vault.RequestedKeyIds.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Dispose_AfterKeyVaultOpen_ZeroesTheSdkReturnedKey()
    {
        var encryptor = CreateEncryptor();
        WrappedDataKey wrapped;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
        }

        var opened = await encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken);
        var sdkKey = Assert.Single(_vault.UnwrapOutputs);
        ByteAssert.NotAllZero(sdkKey);

        opened.Dispose();

        ByteAssert.AllZero(sdkKey);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Dispose_AfterKeyVaultCreate_ZeroesTheDataKeyPassedToWrap()
    {
        var encryptor = CreateEncryptor();

        var session = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);
        var dataKey = Assert.Single(_vault.WrapInputs);
        ByteAssert.NotAllZero(dataKey);

        session.Dispose();

        ByteAssert.AllZero(dataKey);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CreateDataKeyAsync_KeyVaultReturnsOtherKey_ZeroesDataKeyAndThrowsUnexpectedKey()
    {
        var encryptor = CreateEncryptor();
        _vault.WrapKeyIdOverride = EvilKeyId;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken));

        Assert.Equal(EncryptionMessageText.KeyVaultReturnedUnexpectedKey, exception.Message);
        ByteAssert.AllZero(Assert.Single(_vault.WrapInputs));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task OpenDataKeyAsync_KeyVaultReturnsKeyOfWrongLength_ZeroesItAndThrowsMalformedWrappedKey(int length)
    {
        var encryptor = CreateEncryptor();
        WrappedDataKey wrapped;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
        }

        _vault.UnwrapLengthOverride = length;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(() => encryptor.OpenDataKeyAsync(wrapped, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.MalformedWrappedKey, exception.Reason);
        ByteAssert.AllZero(Assert.Single(_vault.UnwrapOutputs));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task OpenDataKeyAsync_ForeignKeyId_ThrowsKeyMismatchAndLogsNoKeyMaterial()
    {
        var encryptor = CreateEncryptor();
        WrappedDataKey wrapped;
        using (var created = await encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken))
        {
            wrapped = created.WrappedKey;
        }

        var foreign = wrapped with { KeyId = EvilKeyId };

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(() => encryptor.OpenDataKeyAsync(foreign, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
        Assert.Equal(0, _vault.UnwrapCount);
        var logged = string.Join('\n', _logger.Entries.Select(entry => entry.Message));
        KeyMaterialLeakAssert.NotLeaked(logged, wrapped.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Exceptions_FromEveryFailurePath_ContainNoKeyMaterial()
    {
        var provider = CreateProvider();
        var dataKey = NewDataKey();
        var dataKeyCopy = dataKey.ToArray();
        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        var tampered = wrapped.Value.ToArray();
        tampered[0] ^= 0x01;
        var failures = new List<Exception>();

        failures.Add(await Capture(() => provider.UnwrapAsync(wrapped with { Value = tampered }, TestContext.Current.CancellationToken)));
        failures.Add(await Capture(() => provider.UnwrapAsync(wrapped with { KeyId = EvilKeyId }, TestContext.Current.CancellationToken)));
        failures.Add(await Capture(() => provider.UnwrapAsync(wrapped with { Algorithm = "RSA1_5" }, TestContext.Current.CancellationToken)));
        failures.Add(await Capture(() => provider.UnwrapAsync(wrapped with { Value = wrapped.Value[..255] }, TestContext.Current.CancellationToken)));
        _vault.WrapKeyIdOverride = EvilKeyId;
        failures.Add(await Capture(() => provider.WrapAsync(dataKeyCopy.ToArray(), TestContext.Current.CancellationToken)));
        _vault.WrapKeyIdOverride = null;
        _vault.WrapLengthOverride = 100;
        failures.Add(await Capture(() => provider.WrapAsync(dataKeyCopy.ToArray(), TestContext.Current.CancellationToken)));
        _vault.WrapLengthOverride = null;
        _vault.UnwrapFailureStatus = 403;
        failures.Add(await Capture(() => provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken)));

        Assert.Equal(7, failures.Count);
        foreach (var failure in failures)
        {
            KeyMaterialLeakAssert.NotLeaked(failure.ToString(), dataKeyCopy, wrapped.Value, tampered);
            if (failure is SecretDecryptionException)
            {
                Assert.Null(failure.InnerException);
                Assert.Empty(failure.Data);
            }
        }
    }

    public void Dispose() => _vault.Dispose();

    private static byte[] NewDataKey() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(DataKeyLength);

    private static async Task<Exception> Capture(Func<Task> action)
    {
        var exception = await Record.ExceptionAsync(action);
        return Assert.IsAssignableFrom<Exception>(exception);
    }

    private KeyVaultKeyWrapProvider CreateProvider() =>
        new(_vault, TestEncryptionOptions.KeyVault(FakeKeyVault.VersionlessKeyId));

    private EnvelopeEncryptor CreateEncryptor() => new(CreateProvider(), _logger);
}
