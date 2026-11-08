// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed class OwnerVaultReads(VaultKeyRingService keyRings) : IDisposable
{
    private readonly Dictionary<Guid, VaultRead> _reads = [];

    public async Task<string?> TryNameAsync(Guid ownerId, Guid secretId, byte[] envelope, int storedVersion, CancellationToken cancellationToken)
    {
        if (!_reads.TryGetValue(ownerId, out var read))
        {
            read = await keyRings.OpenForReadAsync(ownerId, cancellationToken);
            _reads[ownerId] = read;
        }

        return read.Vault is { } vault
            && SecretMetadataCipher.TryDecryptName(vault.Session, secretId, ownerId, envelope, storedVersion, vault.MetadataKeyVersion, out var name)
                ? name
                : null;
    }

    public void Dispose()
    {
        foreach (var read in _reads.Values)
        {
            read.Dispose();
        }

        _reads.Clear();
    }
}
