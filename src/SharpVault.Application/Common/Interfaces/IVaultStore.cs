// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Common.Interfaces;

public interface IVaultStore
{
    Task<Versioned<VaultKeyRing>?> GetKeyRingAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<Versioned<VaultKeyRing>?> AddKeyRingAsync(VaultKeyRing keyRing, CancellationToken cancellationToken);

    Task<Versioned<Secret>?> GetSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);

    Task<Versioned<Secret>?> GetRecycledSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);

    Task<SecretPage> ListSecretsAsync(Guid ownerId, SecretFolderFilter filter, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<Versioned<Secret>>> ListSecretsInFolderAsync(Guid ownerId, Guid? folderId, int limit, CancellationToken cancellationToken);

    Task<Versioned<Folder>?> GetFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);

    Task<Versioned<Folder>?> GetRecycledFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Versioned<Folder>>> ListFoldersAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<Versioned<SecretVersion>?> GetVersionAsync(Guid ownerId, Guid secretId, Guid versionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Versioned<SecretVersion>>> ListVersionsAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);

    Task<RecycleBinPage> ListRecycleBinAsync(Guid ownerId, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<RecycledBatch> ListRecycledBatchAsync(Guid ownerId, Guid deletionBatchId, CancellationToken cancellationToken);

    Task<RecycledBatch> ListRecycledBeforeAsync(Guid ownerId, DateTimeOffset deletedBefore, CancellationToken cancellationToken);

    Task<Versioned<ShareGrant>?> GetShareGrantAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId, CancellationToken cancellationToken);

    Task<ShareGrantRows?> GetShareGrantRowsAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId, CancellationToken cancellationToken);

    Task<string?> GetShareGrantByRecipientETagAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Versioned<ShareGrant>>> ListShareGrantsForItemAsync(Guid ownerId, ShareItemKind kind, Guid itemId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShareGrant>> ListFolderGrantsToRecipientAsync(Guid ownerId, Guid recipientId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShareGrant>> ListGrantsToRecipientBetweenAsync(Guid ownerId, Guid recipientId, ShareItemRef first, ShareItemRef last, CancellationToken cancellationToken);

    Task<ShareGrantPage> ListShareGrantsByOwnerAsync(Guid ownerId, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<StoredCount> CountShareGrantsByOwnerAsync(Guid ownerId, int maxPages, CancellationToken cancellationToken);

    Task<VaultInventoryPage> ListInventoryAsync(Guid ownerId, string? continuationToken, CancellationToken cancellationToken);

    Task<VaultWriteResult> SubmitAsync(Guid ownerId, IReadOnlyList<VaultChange> changes, CancellationToken cancellationToken);
}
