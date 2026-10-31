// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

public sealed class EnvelopeEncryptorTests
{
    private const int ParallelSessions = 64;

    public static TheoryData<int> WrongKeyLengths => new() { 0, 16, 31, 33, 64 };

    [Fact]
    public async Task CreateDataKeyAsync_Always_CallsWrapOnceAndUnwrapNever()
    {
        var harness = new EncryptionHarness();

        using var session = await harness.CreateSessionAsync();

        Assert.Equal(1, harness.Provider.WrapCount);
        Assert.Equal(0, harness.Provider.UnwrapCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CreateDataKeyAsync_TwoSessions_UseDifferentDataKeys()
    {
        var harness = new EncryptionHarness();

        using var first = await harness.CreateSessionAsync();
        using var second = await harness.CreateSessionAsync();

        var keys = harness.Provider.WrapInputs;
        Assert.NotEqual(keys[0], keys[1]);
        Assert.NotEqual(first.WrappedKey.Value, second.WrappedKey.Value);
    }

    [Fact]
    public async Task CreateDataKeyAsync_DataKey_Is32Bytes()
    {
        var harness = new EncryptionHarness();

        using var session = await harness.CreateSessionAsync();

        Assert.Equal(32, harness.Provider.WrapInputs[0].Length);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CreateDataKeyAsync_WrapThrows_ZeroesDataKeyAndPropagates()
    {
        var harness = new EncryptionHarness();
        var failure = new InvalidOperationException("wrap failed");
        harness.Provider.WrapFailure = () => failure;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(harness.CreateSessionAsync);

        Assert.Same(failure, thrown);
        ByteAssert.AllZero(harness.Provider.WrapInputs[0]);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CreateDataKeyAsync_Cancelled_ZeroesDataKeyAndThrowsOperationCanceled()
    {
        var harness = new EncryptionHarness();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Encryptor.CreateDataKeyAsync(source.Token));

        ByteAssert.AllZero(harness.Provider.WrapInputs[0]);
    }

    [Fact]
    public async Task OpenDataKeyAsync_Always_CallsUnwrapOnceAndWrapNever()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        var wraps = harness.Provider.WrapCount;

        using var opened = await harness.OpenSessionAsync(created.WrappedKey);

        Assert.Equal(1, harness.Provider.UnwrapCount);
        Assert.Equal(wraps, harness.Provider.WrapCount);
    }

    [Fact]
    public async Task OpenDataKeyAsync_StoredWrappedKey_DecryptsEnvelopeFromCreatingSession()
    {
        var harness = new EncryptionHarness();
        var context = EncryptionContexts.Random();
        byte[] envelope;
        WrappedDataKey wrapped;
        using (var created = await harness.CreateSessionAsync())
        {
            envelope = created.Encrypt(context, "persisted");
            wrapped = created.WrappedKey;
        }

        using var opened = await harness.OpenSessionAsync(wrapped);

        Assert.Equal("persisted", opened.Decrypt(context, envelope));
        Assert.Same(wrapped, opened.WrappedKey);
    }

    [Theory]
    [MemberData(nameof(WrongKeyLengths))]
    [Trait("Category", "Security")]
    public async Task OpenDataKeyAsync_UnwrappedKeyWrongLength_ZeroesItAndThrowsMalformedWrappedKey(int length)
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        harness.Provider.UnwrapLengthOverride = length;

        var failure = await Assert.ThrowsAsync<SecretDecryptionException>(() => harness.OpenSessionAsync(created.WrappedKey));

        Assert.Equal(DecryptionFailureReason.MalformedWrappedKey, failure.Reason);
        ByteAssert.AllZero(harness.Provider.UnwrapOutputs[0]);
    }

    [Fact]
    public async Task OpenDataKeyAsync_ProviderThrowsDecryptionException_LogsReasonAndRethrowsSameException()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        var failure = new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);
        harness.Provider.UnwrapFailure = () => failure;

        var thrown = await Assert.ThrowsAsync<SecretDecryptionException>(() => harness.OpenSessionAsync(created.WrappedKey));

        Assert.Same(failure, thrown);
        var entry = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(nameof(DecryptionFailureReason.KeyMismatch), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenDataKeyAsync_ProviderThrowsOtherException_PropagatesWithoutLogging()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        var failure = new HttpRequestException("outage");
        harness.Provider.UnwrapFailure = () => failure;

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => harness.OpenSessionAsync(created.WrappedKey));

        Assert.Same(failure, thrown);
        Assert.Empty(harness.Logger.Entries);
    }

    [Fact]
    public async Task OpenDataKeyAsync_UnknownWrappedKey_ThrowsKeyMismatch()
    {
        var harness = new EncryptionHarness();
        var unknown = new WrappedDataKey(RecordingKeyWrapProvider.KeyId, RecordingKeyWrapProvider.Algorithm, new byte[16]);

        var failure = await Assert.ThrowsAsync<SecretDecryptionException>(() => harness.OpenSessionAsync(unknown));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, failure.Reason);
    }

    [Fact]
    public async Task OpenDataKeyAsync_NullWrappedKey_ThrowsArgumentNullException()
    {
        var harness = new EncryptionHarness();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.OpenSessionAsync(null!));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RewrapDataKeyAsync_Always_UnwrapsOnceWrapsOnceAndZeroesKey()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        var wraps = harness.Provider.WrapCount;

        var rewrapped = await harness.Encryptor.RewrapDataKeyAsync(created.WrappedKey, TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.Provider.UnwrapCount);
        Assert.Equal(wraps + 1, harness.Provider.WrapCount);
        ByteAssert.AllZero(harness.Provider.UnwrapOutputs[0]);
        ByteAssert.AllZero(harness.Provider.WrapInputs[1]);
        Assert.NotNull(rewrapped);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RewrapDataKeyAsync_WrapThrows_ZeroesUnwrappedKey()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        var failure = new InvalidOperationException("wrap failed");
        harness.Provider.WrapFailure = () => failure;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Encryptor.RewrapDataKeyAsync(created.WrappedKey, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        ByteAssert.AllZero(harness.Provider.UnwrapOutputs[0]);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RewrapDataKeyAsync_UnwrappedKeyWrongLength_ZeroesItAndThrowsWithoutWrapping()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        var wraps = harness.Provider.WrapCount;
        harness.Provider.UnwrapLengthOverride = 31;

        var failure = await Assert.ThrowsAsync<SecretDecryptionException>(() =>
            harness.Encryptor.RewrapDataKeyAsync(created.WrappedKey, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.MalformedWrappedKey, failure.Reason);
        Assert.Equal(wraps, harness.Provider.WrapCount);
        ByteAssert.AllZero(harness.Provider.UnwrapOutputs[0]);
    }

    [Fact]
    public async Task RewrapDataKeyAsync_Result_OpensToSameDataKey()
    {
        var harness = new EncryptionHarness();
        var context = EncryptionContexts.Random();
        byte[] envelope;
        WrappedDataKey original;
        using (var created = await harness.CreateSessionAsync())
        {
            envelope = created.Encrypt(context, "survives-rewrap");
            original = created.WrappedKey;
        }

        var rewrapped = await harness.Encryptor.RewrapDataKeyAsync(original, TestContext.Current.CancellationToken);
        using var opened = await harness.OpenSessionAsync(rewrapped);

        Assert.NotEqual(original.Value, rewrapped.Value);
        Assert.Equal("survives-rewrap", opened.Decrypt(context, envelope));
    }

    [Fact]
    public async Task RewrapDataKeyAsync_NullWrappedKey_ThrowsArgumentNullException()
    {
        var harness = new EncryptionHarness();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Encryptor.RewrapDataKeyAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Sessions_UsedInParallel_EachRoundTrips()
    {
        var harness = new EncryptionHarness();

        var results = await Task.WhenAll(Enumerable.Range(0, ParallelSessions).Select(async index =>
        {
            await Task.Yield();
            var context = EncryptionContexts.Random();
            var text = $"value-{index}";
            WrappedDataKey wrapped;
            byte[] envelope;
            using (var created = await harness.CreateSessionAsync())
            {
                envelope = created.Encrypt(context, text);
                wrapped = created.WrappedKey;
            }

            using var opened = await harness.OpenSessionAsync(wrapped);
            return opened.Decrypt(context, envelope) == text;
        }));

        Assert.All(results, Assert.True);
        Assert.Equal(ParallelSessions, harness.Provider.WrapCount);
        Assert.Equal(ParallelSessions, harness.Provider.UnwrapCount);
    }
}
