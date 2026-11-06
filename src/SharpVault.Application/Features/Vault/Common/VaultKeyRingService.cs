// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.Common;

public sealed partial class VaultKeyRingService(IVaultStore store, IEnvelopeEncryptor encryptor, TimeProvider timeProvider, ILogger<VaultKeyRingService> logger)
{
    private const string KeyRingMissingMessage = "The vault key ring could not be created or read.";

    public async Task<VaultRead> OpenForReadAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var ring = await store.GetKeyRingAsync(ownerId, cancellationToken);
        if (ring is null)
        {
            return VaultRead.NoRing;
        }

        try
        {
            var session = await encryptor.OpenDataKeyAsync(ring.Value.MetadataKey, cancellationToken);
            return VaultRead.Opened(new OpenedVault(session, ring.Value.MetadataKeyVersion, ring.ETag));
        }
        catch (SecretDecryptionException exception)
        {
            LogKeyRingUnreadable(logger, ownerId, exception.Reason);
            return VaultRead.Unreadable;
        }
    }

    public async Task<OpenedVault?> OpenAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var ring = await store.GetKeyRingAsync(ownerId, cancellationToken);
        if (ring is null)
        {
            return null;
        }

        var session = await encryptor.OpenDataKeyAsync(ring.Value.MetadataKey, cancellationToken);
        return new OpenedVault(session, ring.Value.MetadataKeyVersion, ring.ETag);
    }

    public async Task<OpenedVault> OpenOrCreateAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var existing = await OpenAsync(ownerId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var session = await encryptor.CreateDataKeyAsync(cancellationToken);
        try
        {
            var keyRing = new VaultKeyRing
            {
                OwnerId = ownerId,
                MetadataKey = session.WrappedKey,
                MetadataKeyVersion = VaultLimits.CurrentMetadataKeyVersion,
                CreatedAt = timeProvider.GetUtcNow(),
            };

            var added = await store.AddKeyRingAsync(keyRing, cancellationToken);
            if (added is not null)
            {
                return new OpenedVault(session, keyRing.MetadataKeyVersion, added.ETag);
            }
        }
        catch
        {
            session.Dispose();
            throw;
        }

        session.Dispose();
        return await OpenAsync(ownerId, cancellationToken) ?? throw new InvalidOperationException(KeyRingMissingMessage);
    }

    public async Task<string?> GetKeyRingETagAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var ring = await store.GetKeyRingAsync(ownerId, cancellationToken);
        return ring?.ETag;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The vault key ring of owner {OwnerId} could not be opened: {Reason}")]
    private static partial void LogKeyRingUnreadable(ILogger logger, Guid ownerId, DecryptionFailureReason reason);
}
