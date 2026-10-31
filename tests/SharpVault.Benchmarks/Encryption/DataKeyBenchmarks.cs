// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using BenchmarkDotNet.Attributes;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Benchmarks.Support;
using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;

namespace SharpVault.Benchmarks.Encryption;

[MemoryDiagnoser]
public class DataKeyBenchmarks
{
    private const int KibibyteLength = 1024;

    private static readonly EncryptionContext Context = new("secret.value", Guid.NewGuid(), Guid.NewGuid());

    private EnvelopeEncryptor? _encryptor;
    private WrappedDataKey? _wrappedKey;
    private byte[] _envelope = [];

    private EnvelopeEncryptor Encryptor => _encryptor ?? throw new InvalidOperationException("The benchmark encryptor has not been created.");

    private WrappedDataKey WrappedKey => _wrappedKey ?? throw new InvalidOperationException("The benchmark data key has not been created.");

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _encryptor = BenchmarkEncryption.CreateDevelopmentEncryptor();
        using var session = await Encryptor.CreateDataKeyAsync(CancellationToken.None);
        _wrappedKey = session.WrappedKey;
        _envelope = session.Encrypt(Context, new string('a', KibibyteLength));
    }

    [Benchmark]
    public async Task CreateDataKey()
    {
        using var session = await Encryptor.CreateDataKeyAsync(CancellationToken.None);
    }

    [Benchmark]
    public async Task OpenDataKey()
    {
        using var session = await Encryptor.OpenDataKeyAsync(WrappedKey, CancellationToken.None);
    }

    [Benchmark]
    public async Task<WrappedDataKey> RewrapDataKey() => await Encryptor.RewrapDataKeyAsync(WrappedKey, CancellationToken.None);

    [Benchmark]
    public async Task<string> OpenAndDecryptOneKibibyte()
    {
        using var session = await Encryptor.OpenDataKeyAsync(WrappedKey, CancellationToken.None);
        return session.Decrypt(Context, _envelope);
    }
}
