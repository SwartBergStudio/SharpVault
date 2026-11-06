// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.Vault;

public sealed record SeededSecret(Guid Id, string Name, string Value, string Notes);

public sealed record SeededFolder(Guid Id, string Name);

public sealed record SeedPlacement(Guid? ParentFolderId, Guid? SecretFolderId);

public sealed record SeededVault(Guid Owner, IReadOnlyList<SeededSecret> Secrets, IReadOnlyList<SeededFolder> Folders);

public static class VaultSeeder
{
    private const int ChunkSize = 50;

    public static Task<SeededVault> SeedAsync(
        ApiTestFactory factory,
        Guid owner,
        int secretCount,
        int folderCount,
        CancellationToken cancellationToken) =>
        SeedAsync(factory, owner, secretCount, folderCount, new SeedPlacement(null, null), cancellationToken);

    public static async Task<SeededVault> SeedAsync(
        ApiTestFactory factory,
        Guid owner,
        int secretCount,
        int folderCount,
        SeedPlacement placement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(placement);

        var services = factory.Services;
        var keyRings = services.GetRequiredService<VaultKeyRingService>();
        var encryptor = services.GetRequiredService<IEnvelopeEncryptor>();
        var store = services.GetRequiredService<IVaultStore>();
        var time = services.GetRequiredService<TimeProvider>();

        using var vault = await keyRings.OpenOrCreateAsync(owner, cancellationToken);
        var changes = new List<VaultChange>();
        var folders = new List<SeededFolder>();
        for (var index = 0; index < folderCount; index++)
        {
            var name = $"SV-FOLDER-{Guid.NewGuid():N}";
            var now = time.GetUtcNow();
            var folder = new Folder
            {
                Id = VaultIds.NewId(time),
                OwnerId = owner,
                ParentId = placement.ParentFolderId,
                MetadataKeyVersion = vault.MetadataKeyVersion,
                CreatedAt = now,
                ModifiedAt = now,
            };
            SecretMetadataCipher.EncryptFolderName(vault.Session, folder, name);
            changes.Add(VaultChange.AddFolder(folder));
            folders.Add(new SeededFolder(folder.Id, name));
        }

        var secrets = new List<SeededSecret>();
        for (var index = 0; index < secretCount; index++)
        {
            using var secretKey = await encryptor.CreateDataKeyAsync(cancellationToken);
            var markers = new PlaintextMarkers();
            var now = time.GetUtcNow();
            var secret = new Secret
            {
                Id = VaultIds.NewId(time),
                OwnerId = owner,
                Type = SecretType.Login,
                FolderId = placement.SecretFolderId,
                DataKey = secretKey.WrappedKey,
                MetadataKeyVersion = vault.MetadataKeyVersion,
                Revision = 1,
                CreatedAt = now,
                ModifiedAt = now,
            };
            SecretMetadataCipher.Encrypt(vault.Session, secret, new SecretMetadata(markers.Name, markers.Username, markers.Url, [markers.Tag]));
            secret.ValueEnvelope = SecretValueCipher.EncryptValue(secretKey, secret, markers.Value);
            secret.NotesEnvelope = SecretValueCipher.EncryptNotes(secretKey, secret, markers.Notes);
            changes.Add(VaultChange.AddSecret(secret));
            secrets.Add(new SeededSecret(secret.Id, markers.Name, markers.Value, markers.Notes));
        }

        foreach (var chunk in changes.Chunk(ChunkSize))
        {
            var result = await store.SubmitAsync(owner, chunk, cancellationToken);
            if (result.Result != StoreWriteResult.Written)
            {
                throw new InvalidOperationException($"Seeding the vault failed with {result.Result}.");
            }
        }

        return new SeededVault(owner, secrets, folders);
    }
}
