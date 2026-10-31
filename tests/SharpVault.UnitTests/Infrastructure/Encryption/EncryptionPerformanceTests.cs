// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

[Trait("Category", "Performance")]
[Collection(nameof(EncryptionPerformanceTestGroup))]
public sealed class EncryptionPerformanceTests
{
    private const int KibibyteLength = 1024;
    private const int Iterations = 10_000;
    private const int MaxSizeIterations = 1_000;
    private const int DataKeyIterations = 1_000;
    private const int KeyVaultIterations = 20;
    private const int WarmUpIterations = 200;
    private const int NonceCount = 100_000;
    private const int AllocationSlack = 64;
    private const double EncryptBudgetMicroseconds = 50;
    private const double DecryptBudgetMicroseconds = 50;
    private const double ReEncryptBudgetMicroseconds = 100;
    private const double MaxSizeBudgetMicroseconds = 500;
    private const double CreateDataKeyBudgetMicroseconds = 200;
    private const double OpenDataKeyBudgetMilliseconds = 20;
    private const double NonceBudgetSeconds = 5;
    private const double MicrosecondsPerMillisecond = 1000;

    private static readonly EncryptionContext Source = new("secret.value", Guid.NewGuid(), Guid.NewGuid());
    private static readonly EncryptionContext Target = new("secret.value", Source.ItemId, Guid.NewGuid());

    [Fact]
    public async Task Encrypt_OneKibibyteTenThousandTimes_AveragesUnder50Microseconds()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var plaintext = new string('a', KibibyteLength);
        Repeat(WarmUpIterations, () => session.Encrypt(Source, plaintext));

        var average = AverageMicroseconds(Iterations, () => session.Encrypt(Source, plaintext));

        Assert.True(average < EncryptBudgetMicroseconds, $"Average {average:F2} us exceeds {EncryptBudgetMicroseconds} us.");
    }

    [Fact]
    public async Task Decrypt_OneKibibyteTenThousandTimes_AveragesUnder50Microseconds()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var envelope = session.Encrypt(Source, new string('a', KibibyteLength));
        Repeat(WarmUpIterations, () => session.Decrypt(Source, envelope));

        var average = AverageMicroseconds(Iterations, () => session.Decrypt(Source, envelope));

        Assert.True(average < DecryptBudgetMicroseconds, $"Average {average:F2} us exceeds {DecryptBudgetMicroseconds} us.");
    }

    [Fact]
    public async Task ReEncrypt_OneKibibyteTenThousandTimes_AveragesUnder100Microseconds()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var envelope = session.Encrypt(Source, new string('a', KibibyteLength));
        Repeat(WarmUpIterations, () => session.ReEncrypt(Source, Target, envelope));

        var average = AverageMicroseconds(Iterations, () => session.ReEncrypt(Source, Target, envelope));

        Assert.True(average < ReEncryptBudgetMicroseconds, $"Average {average:F2} us exceeds {ReEncryptBudgetMicroseconds} us.");
    }

    [Fact]
    public async Task EncryptAndDecrypt_MaximumSizeThousandTimes_AverageUnder500Microseconds()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var plaintext = new string('a', EnvelopeFormat.MaxPlaintextBytes);
        Repeat(WarmUpIterations / 10, () => session.Decrypt(Source, session.Encrypt(Source, plaintext)));

        var average = AverageMicroseconds(MaxSizeIterations, () => session.Decrypt(Source, session.Encrypt(Source, plaintext)));

        Assert.True(average < MaxSizeBudgetMicroseconds, $"Average {average:F2} us exceeds {MaxSizeBudgetMicroseconds} us.");
    }

    [Fact]
    public async Task CreateDataKeyAsync_DevelopmentProviderThousandTimes_AveragesUnder200Microseconds()
    {
        var encryptor = CreateDevelopmentEncryptor();
        var token = TestContext.Current.CancellationToken;
        for (var i = 0; i < WarmUpIterations; i++)
        {
            (await encryptor.CreateDataKeyAsync(token)).Dispose();
        }

        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < DataKeyIterations; i++)
        {
            (await encryptor.CreateDataKeyAsync(token)).Dispose();
        }

        var average = Stopwatch.GetElapsedTime(start).TotalMicroseconds / DataKeyIterations;

        Assert.True(average < CreateDataKeyBudgetMicroseconds, $"Average {average:F2} us exceeds {CreateDataKeyBudgetMicroseconds} us.");
    }

    [Fact]
    public async Task OpenDataKeyAsync_RealSdkWithInProcessTransport_AveragesUnder20Milliseconds()
    {
        using var vault = new PerformanceKeyVaultHandler();
        var encryptor = CreateKeyVaultEncryptor(vault);
        var token = TestContext.Current.CancellationToken;
        using var created = await encryptor.CreateDataKeyAsync(token);
        var wrapped = created.WrappedKey;
        (await encryptor.OpenDataKeyAsync(wrapped, token)).Dispose();

        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < KeyVaultIterations; i++)
        {
            (await encryptor.OpenDataKeyAsync(wrapped, token)).Dispose();
        }

        var average = Stopwatch.GetElapsedTime(start).TotalMilliseconds / KeyVaultIterations;

        Assert.True(average < OpenDataKeyBudgetMilliseconds, $"Average {average:F2} ms exceeds {OpenDataKeyBudgetMilliseconds} ms.");
    }

    [Fact]
    public async Task Encrypt_OneKibibyte_AllocatesOnlyTheEnvelope()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var plaintext = new string('a', KibibyteLength);
        Repeat(WarmUpIterations, () => session.Encrypt(Source, plaintext));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var envelope = session.Encrypt(Source, plaintext);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= envelope.Length + AllocationSlack, $"Allocated {allocated} bytes for an envelope of {envelope.Length}.");
    }

    [Fact]
    public async Task Decrypt_OneKibibyte_AllocatesOnlyTheString()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var envelope = session.Encrypt(Source, new string('a', KibibyteLength));
        Repeat(WarmUpIterations, () => session.Decrypt(Source, envelope));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var text = session.Decrypt(Source, envelope);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(KibibyteLength, text.Length);
        Assert.True(allocated <= (2 * KibibyteLength) + AllocationSlack, $"Allocated {allocated} bytes.");
    }

    [Fact]
    public async Task ReEncrypt_OneKibibyte_AllocatesOnlyTheNewEnvelope()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var envelope = session.Encrypt(Source, new string('a', KibibyteLength));
        Repeat(WarmUpIterations, () => session.ReEncrypt(Source, Target, envelope));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var reEncrypted = session.ReEncrypt(Source, Target, envelope);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= reEncrypted.Length + AllocationSlack, $"Allocated {allocated} bytes for an envelope of {reEncrypted.Length}.");
    }

    [Fact]
    public async Task Encrypt_HundredThousandTimes_FinishesUnder5SecondsWithUniqueNonces()
    {
        using var session = await CreateDevelopmentSessionAsync();
        var nonces = new HashSet<string>(NonceCount, StringComparer.Ordinal);
        session.Encrypt(Source, "warm-up");

        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < NonceCount; i++)
        {
            var envelope = session.Encrypt(Source, "x");
            nonces.Add(Convert.ToHexString(envelope.AsSpan(1, EnvelopeFormat.NonceSize)));
        }

        var elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(NonceCount, nonces.Count);
        Assert.True(elapsed.TotalSeconds < NonceBudgetSeconds, $"Took {elapsed.TotalSeconds:F2} s.");
    }

    [Fact]
    public async Task Session_EncryptDecryptAndReEncryptThousandTimes_MakesNoWrapOrUnwrapCalls()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var wrapsAfterCreate = harness.Provider.WrapCount;
        var plaintext = new string('a', KibibyteLength);

        for (var i = 0; i < DataKeyIterations; i++)
        {
            var envelope = session.Encrypt(Source, plaintext);
            session.Decrypt(Source, envelope);
            session.ReEncrypt(Source, Target, envelope);
        }

        Assert.Equal(1, wrapsAfterCreate);
        Assert.Equal(1, harness.Provider.WrapCount);
        Assert.Equal(0, harness.Provider.UnwrapCount);
    }

    [Fact]
    public async Task Dispose_Session_MakesNoWrapOrUnwrapCalls()
    {
        var harness = new EncryptionHarness();
        var session = await harness.CreateSessionAsync();

        session.Dispose();

        Assert.Equal(1, harness.Provider.WrapCount);
        Assert.Equal(0, harness.Provider.UnwrapCount);
    }

    [Fact]
    public async Task OpenDataKeyAsync_ThenManyOperations_UnwrapsExactlyOnce()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();
        using var opened = await harness.OpenSessionAsync(created.WrappedKey);

        for (var i = 0; i < DataKeyIterations; i++)
        {
            opened.Decrypt(Source, opened.Encrypt(Source, "value"));
        }

        Assert.Equal(1, harness.Provider.WrapCount);
        Assert.Equal(1, harness.Provider.UnwrapCount);
    }

    [Fact]
    public async Task RewrapDataKeyAsync_RecordingProvider_UnwrapsOnceAndWrapsOnce()
    {
        var harness = new EncryptionHarness();
        using var created = await harness.CreateSessionAsync();

        await harness.Encryptor.RewrapDataKeyAsync(created.WrappedKey, TestContext.Current.CancellationToken);

        Assert.Equal(2, harness.Provider.WrapCount);
        Assert.Equal(1, harness.Provider.UnwrapCount);
    }

    [Fact]
    public async Task KeyVaultCalls_CreateDataKeyAsyncReadableKeyTwice_ReadsKeyOnceAndNeverWrapsRemotely()
    {
        using var vault = new PerformanceKeyVaultHandler();
        var encryptor = CreateKeyVaultEncryptor(vault);
        var token = TestContext.Current.CancellationToken;

        (await encryptor.CreateDataKeyAsync(token)).Dispose();
        (await encryptor.CreateDataKeyAsync(token)).Dispose();

        Assert.Equal(1, vault.KeyReadRequests);
        Assert.Equal(0, vault.WrapKeyRequests);
        Assert.Equal(0, vault.UnwrapKeyRequests);
    }

    [Fact]
    public async Task KeyVaultCalls_CreateDataKeyAsyncKeyReadForbidden_SendsAtMostOneWrapKeyPerCall()
    {
        using var vault = new PerformanceKeyVaultHandler { AllowKeyRead = false };
        var encryptor = CreateKeyVaultEncryptor(vault);
        var token = TestContext.Current.CancellationToken;
        (await encryptor.CreateDataKeyAsync(token)).Dispose();
        vault.ResetCounts();

        (await encryptor.CreateDataKeyAsync(token)).Dispose();

        Assert.Equal(1, vault.WrapKeyRequests);
        Assert.Equal(0, vault.UnwrapKeyRequests);
    }

    [Fact]
    public async Task KeyVaultCalls_OpenDataKeyAsync_SendsExactlyOneUnwrapKeyPerCall()
    {
        using var vault = new PerformanceKeyVaultHandler();
        var encryptor = CreateKeyVaultEncryptor(vault);
        var token = TestContext.Current.CancellationToken;
        using var created = await encryptor.CreateDataKeyAsync(token);
        (await encryptor.OpenDataKeyAsync(created.WrappedKey, token)).Dispose();
        vault.ResetCounts();

        (await encryptor.OpenDataKeyAsync(created.WrappedKey, token)).Dispose();
        (await encryptor.OpenDataKeyAsync(created.WrappedKey, token)).Dispose();

        Assert.Equal(2, vault.UnwrapKeyRequests);
        Assert.Equal(0, vault.WrapKeyRequests);
        Assert.Equal(0, vault.KeyReadRequests);
    }

    [Fact]
    public async Task KeyVaultCalls_RewrapDataKeyAsync_SendsOneUnwrapKeyAndAtMostOneWrapKey()
    {
        using var vault = new PerformanceKeyVaultHandler { AllowKeyRead = false };
        var encryptor = CreateKeyVaultEncryptor(vault);
        var token = TestContext.Current.CancellationToken;
        using var created = await encryptor.CreateDataKeyAsync(token);
        vault.ResetCounts();

        await encryptor.RewrapDataKeyAsync(created.WrappedKey, token);

        Assert.Equal(1, vault.UnwrapKeyRequests);
        Assert.InRange(vault.WrapKeyRequests, 0, 1);
    }

    [Fact]
    public async Task KeyVaultCalls_SessionOperationsAndDispose_MakeNoRequests()
    {
        using var vault = new PerformanceKeyVaultHandler();
        var encryptor = CreateKeyVaultEncryptor(vault);
        var token = TestContext.Current.CancellationToken;
        var session = await encryptor.CreateDataKeyAsync(token);
        vault.ResetCounts();

        for (var i = 0; i < DataKeyIterations; i++)
        {
            var envelope = session.Encrypt(Source, "value");
            session.Decrypt(Source, envelope);
            session.ReEncrypt(Source, Target, envelope);
        }

        session.Dispose();

        Assert.Equal(0, vault.TotalRequests);
    }

    private static EnvelopeEncryptor CreateDevelopmentEncryptor()
    {
        var provider = new DevelopmentKeyWrapProvider(HostEnvironments.Named("Development"), TestEncryptionOptions.Development());
        return new EnvelopeEncryptor(provider, NullLogger<EnvelopeEncryptor>.Instance);
    }

    private static EnvelopeEncryptor CreateKeyVaultEncryptor(PerformanceKeyVaultHandler vault)
    {
        var provider = new KeyVaultKeyWrapProvider(vault, TestEncryptionOptions.KeyVault(PerformanceKeyVaultHandler.VersionlessKeyId));
        return new EnvelopeEncryptor(provider, NullLogger<EnvelopeEncryptor>.Instance);
    }

    private static async Task<IDataKeySession> CreateDevelopmentSessionAsync() =>
        await CreateDevelopmentEncryptor().CreateDataKeyAsync(TestContext.Current.CancellationToken);

    private static void Repeat(int count, Action action)
    {
        for (var i = 0; i < count; i++)
        {
            action();
        }
    }

    private static double AverageMicroseconds(int count, Action action)
    {
        var start = Stopwatch.GetTimestamp();
        Repeat(count, action);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds * MicrosecondsPerMillisecond / count;
    }
}
