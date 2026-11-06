// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using BenchmarkDotNet.Attributes;
using Mapster;
using MapsterMapper;
using SharpVault.Benchmarks.Support;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Benchmarks.Vault;

[MemoryDiagnoser]
public class VaultEntityMappingBenchmarks
{
    private readonly Mapper _mapper = CreateMapper();

    private Secret? _secret;
    private SecretTableEntity? _entity;

    private Secret Secret => _secret ?? throw new InvalidOperationException("The benchmark secret has not been created.");

    private SecretTableEntity Entity => _entity ?? throw new InvalidOperationException("The benchmark entity has not been created.");

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var encryptor = BenchmarkEncryption.CreateDevelopmentEncryptor();
        using var metadataSession = await encryptor.CreateDataKeyAsync(CancellationToken.None);
        _secret = await BenchmarkSecrets.CreateAsync(encryptor, metadataSession, 0);
        _entity = _mapper.Map<SecretTableEntity>(_secret);
    }

    [Benchmark]
    public object SecretToTableEntity() => _mapper.Map<SecretTableEntity>(Secret);

    [Benchmark]
    public object TableEntityToSecret() => _mapper.Map<Secret>(Entity);

    private static Mapper CreateMapper()
    {
        var config = new TypeAdapterConfig();
        new VaultStorageMappingRegister().Register(config);
        return new Mapper(config);
    }
}
