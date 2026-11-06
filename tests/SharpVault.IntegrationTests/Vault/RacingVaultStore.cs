// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;

namespace SharpVault.IntegrationTests.Vault;

public sealed class RacingVaultStore(IVaultStore inner) : IVaultStore
{
    private Func<RecycledBatch, CancellationToken, Task>? _afterNextBatchRead;
    private Func<IReadOnlyList<Versioned<Secret>>, CancellationToken, Task>? _afterNextFolderSecretsRead;

    public void AfterNextBatchRead(Func<RecycledBatch, CancellationToken, Task> hook) => _afterNextBatchRead = hook;

    public void AfterNextFolderSecretsRead(Func<IReadOnlyList<Versioned<Secret>>, CancellationToken, Task> hook) => _afterNextFolderSecretsRead = hook;

    public async Task<RecycledBatch> ListRecycledBatchAsync(Guid ownerId, Guid deletionBatchId, CancellationToken cancellationToken)
    {
        var batch = await inner.ListRecycledBatchAsync(ownerId, deletionBatchId, cancellationToken);
        var hook = Interlocked.Exchange(ref _afterNextBatchRead, null);
        if (hook is not null)
        {
            await hook(batch, cancellationToken);
        }

        return batch;
    }

    public Task<Versioned<VaultKeyRing>?> GetKeyRingAsync(Guid ownerId, CancellationToken cancellationToken) =>
        inner.GetKeyRingAsync(ownerId, cancellationToken);

    public Task<Versioned<VaultKeyRing>?> AddKeyRingAsync(VaultKeyRing keyRing, CancellationToken cancellationToken) =>
        inner.AddKeyRingAsync(keyRing, cancellationToken);

    public Task<Versioned<Secret>?> GetSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        inner.GetSecretAsync(ownerId, secretId, cancellationToken);

    public Task<Versioned<Secret>?> GetRecycledSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        inner.GetRecycledSecretAsync(ownerId, secretId, cancellationToken);

    public Task<SecretPage> ListSecretsAsync(Guid ownerId, SecretFolderFilter filter, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        inner.ListSecretsAsync(ownerId, filter, pageSize, continuationToken, cancellationToken);

    public async Task<IReadOnlyList<Versioned<Secret>>> ListSecretsInFolderAsync(Guid ownerId, Guid? folderId, int limit, CancellationToken cancellationToken)
    {
        var secrets = await inner.ListSecretsInFolderAsync(ownerId, folderId, limit, cancellationToken);
        if (secrets.Count > 0 && Interlocked.Exchange(ref _afterNextFolderSecretsRead, null) is { } hook)
        {
            await hook(secrets, cancellationToken);
        }

        return secrets;
    }

    public Task<Versioned<Folder>?> GetFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        inner.GetFolderAsync(ownerId, folderId, cancellationToken);

    public Task<Versioned<Folder>?> GetRecycledFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        inner.GetRecycledFolderAsync(ownerId, folderId, cancellationToken);

    public Task<IReadOnlyList<Versioned<Folder>>> ListFoldersAsync(Guid ownerId, CancellationToken cancellationToken) =>
        inner.ListFoldersAsync(ownerId, cancellationToken);

    public Task<Versioned<SecretVersion>?> GetVersionAsync(Guid ownerId, Guid secretId, Guid versionId, CancellationToken cancellationToken) =>
        inner.GetVersionAsync(ownerId, secretId, versionId, cancellationToken);

    public Task<IReadOnlyList<Versioned<SecretVersion>>> ListVersionsAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        inner.ListVersionsAsync(ownerId, secretId, cancellationToken);

    public Task<RecycleBinPage> ListRecycleBinAsync(Guid ownerId, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        inner.ListRecycleBinAsync(ownerId, pageSize, continuationToken, cancellationToken);

    public Task<RecycledBatch> ListRecycledBeforeAsync(Guid ownerId, DateTimeOffset deletedBefore, CancellationToken cancellationToken) =>
        inner.ListRecycledBeforeAsync(ownerId, deletedBefore, cancellationToken);

    public Task<Versioned<ShareGrant>?> GetShareGrantAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId, CancellationToken cancellationToken) =>
        inner.GetShareGrantAsync(ownerId, kind, itemId, recipientId, cancellationToken);

    public Task<ShareGrantRows?> GetShareGrantRowsAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId, CancellationToken cancellationToken) =>
        inner.GetShareGrantRowsAsync(ownerId, kind, itemId, recipientId, cancellationToken);

    public Task<string?> GetShareGrantByRecipientETagAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId, CancellationToken cancellationToken) =>
        inner.GetShareGrantByRecipientETagAsync(ownerId, kind, itemId, recipientId, cancellationToken);

    public Task<IReadOnlyList<Versioned<ShareGrant>>> ListShareGrantsForItemAsync(Guid ownerId, ShareItemKind kind, Guid itemId, int limit, CancellationToken cancellationToken) =>
        inner.ListShareGrantsForItemAsync(ownerId, kind, itemId, limit, cancellationToken);

    public Task<IReadOnlyList<ShareGrant>> ListFolderGrantsToRecipientAsync(Guid ownerId, Guid recipientId, CancellationToken cancellationToken) =>
        inner.ListFolderGrantsToRecipientAsync(ownerId, recipientId, cancellationToken);

    public Task<IReadOnlyList<ShareGrant>> ListGrantsToRecipientBetweenAsync(Guid ownerId, Guid recipientId, ShareItemRef first, ShareItemRef last, CancellationToken cancellationToken) =>
        inner.ListGrantsToRecipientBetweenAsync(ownerId, recipientId, first, last, cancellationToken);

    public Task<ShareGrantPage> ListShareGrantsByOwnerAsync(Guid ownerId, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        inner.ListShareGrantsByOwnerAsync(ownerId, pageSize, continuationToken, cancellationToken);

    public Task<StoredCount> CountShareGrantsByOwnerAsync(Guid ownerId, int maxPages, CancellationToken cancellationToken) =>
        inner.CountShareGrantsByOwnerAsync(ownerId, maxPages, cancellationToken);

    public Task<VaultInventoryPage> ListInventoryAsync(Guid ownerId, string? continuationToken, CancellationToken cancellationToken) =>
        inner.ListInventoryAsync(ownerId, continuationToken, cancellationToken);

    public Task<VaultWriteResult> SubmitAsync(Guid ownerId, IReadOnlyList<VaultChange> changes, CancellationToken cancellationToken) =>
        inner.SubmitAsync(ownerId, changes, cancellationToken);
}
