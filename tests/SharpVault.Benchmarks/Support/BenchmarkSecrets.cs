// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;

namespace SharpVault.Benchmarks.Support;

internal static class BenchmarkSecrets
{
    public const int MetadataKeyVersion = 1;

    public static readonly Guid Owner = Guid.NewGuid();

    public static SecretMetadata MetadataFor(int index) => new(
        $"Benchmark secret {index}",
        $"user{index}@example.test",
        $"https://example.test/login/{index}",
        ["benchmark", $"group-{index % 7}"]);

    public static async Task<Secret> CreateAsync(IEnvelopeEncryptor encryptor, IDataKeySession metadataSession, int index)
    {
        using var secretKey = await encryptor.CreateDataKeyAsync(CancellationToken.None);
        var now = TimeProvider.System.GetUtcNow();
        var secret = new Secret
        {
            Id = VaultIds.NewId(TimeProvider.System),
            OwnerId = Owner,
            Type = SecretType.Login,
            DataKey = secretKey.WrappedKey,
            MetadataKeyVersion = MetadataKeyVersion,
            Revision = 1,
            CreatedAt = now,
            ModifiedAt = now,
        };
        SecretMetadataCipher.Encrypt(metadataSession, secret, MetadataFor(index));
        secret.ValueEnvelope = SecretValueCipher.EncryptValue(secretKey, secret, "benchmark-value");
        secret.NotesEnvelope = SecretValueCipher.EncryptNotes(secretKey, secret, "benchmark-notes");
        return secret;
    }
}
