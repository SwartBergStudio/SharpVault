// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using BenchmarkDotNet.Attributes;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Benchmarks.Support;

namespace SharpVault.Benchmarks.Encryption;

[MemoryDiagnoser]
public class EnvelopeEncryptionBenchmarks
{
    private static readonly EncryptionContext Source = new("secret.value", Guid.NewGuid(), Guid.NewGuid());
    private static readonly EncryptionContext Target = new("secret.value", Source.ItemId, Guid.NewGuid());

    private IDataKeySession? _session;
    private string _plaintext = string.Empty;
    private byte[] _envelope = [];

    [Params(32, 1024, 32768)]
    public int PlaintextBytes { get; set; }

    private IDataKeySession Session => _session ?? throw new InvalidOperationException("The benchmark session has not been created.");

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _session = await BenchmarkEncryption.CreateDevelopmentEncryptor().CreateDataKeyAsync(CancellationToken.None);
        _plaintext = new string('a', PlaintextBytes);
        _envelope = Session.Encrypt(Source, _plaintext);
    }

    [GlobalCleanup]
    public void Cleanup() => _session?.Dispose();

    [Benchmark]
    public byte[] Encrypt() => Session.Encrypt(Source, _plaintext);

    [Benchmark]
    public string Decrypt() => Session.Decrypt(Source, _envelope);

    [Benchmark]
    public byte[] ReEncrypt() => Session.ReEncrypt(Source, Target, _envelope);
}
