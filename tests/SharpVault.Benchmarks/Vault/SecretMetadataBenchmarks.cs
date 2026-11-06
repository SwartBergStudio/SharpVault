// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using BenchmarkDotNet.Attributes;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Benchmarks.Support;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Encryption;

namespace SharpVault.Benchmarks.Vault;

[MemoryDiagnoser]
public class SecretMetadataBenchmarks
{
    private const int PageSize = 50;

    private EnvelopeEncryptor? _encryptor;
    private IDataKeySession? _metadataSession;
    private Secret[] _page = [];

    private EnvelopeEncryptor Encryptor => _encryptor ?? throw new InvalidOperationException("The benchmark encryptor has not been created.");

    private IDataKeySession MetadataSession => _metadataSession ?? throw new InvalidOperationException("The benchmark metadata key has not been created.");

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _encryptor = BenchmarkEncryption.CreateDevelopmentEncryptor();
        _metadataSession = await Encryptor.CreateDataKeyAsync(CancellationToken.None);
        _page = new Secret[PageSize];
        for (var index = 0; index < PageSize; index++)
        {
            _page[index] = await BenchmarkSecrets.CreateAsync(Encryptor, MetadataSession, index);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _metadataSession?.Dispose();

    [Benchmark]
    public int DecryptPageOf50Summaries()
    {
        var decrypted = 0;
        foreach (var secret in _page)
        {
            if (SecretMetadataCipher.TryDecrypt(MetadataSession, secret, BenchmarkSecrets.MetadataKeyVersion, out _))
            {
                decrypted++;
            }
        }

        return decrypted;
    }

    [Benchmark]
    public async Task<Secret> EncryptSecretForCreate() => await BenchmarkSecrets.CreateAsync(Encryptor, MetadataSession, 0);
}
