// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.RecycleBin;

public sealed partial class RecycledItemEraser(
    IVaultStore store,
    IShareInboxStore inbox,
    TimeProvider timeProvider,
    ILogger<RecycledItemEraser> logger)
{
    private const int GrantListLimit = SharingLimits.GrantReadLimit;

    public async Task<ErasedItems> EraseSecretAsync(Guid ownerId, Versioned<Secret> recycled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recycled);

        var grants = await ReadGrantRowsAsync(ownerId, ShareItemKind.Secret, recycled.Value.Id, cancellationToken);
        var versions = await store.ListVersionsAsync(ownerId, recycled.Value.Id, cancellationToken);

        var changes = new List<VaultChange>();
        changes.AddRange(grants.Select(DeleteChange));
        changes.AddRange(versions.Select(version => VaultChange.DeleteVersion(version.Value, version.ETag)));
        changes.Add(VaultChange.PurgeSecret(recycled.Value, recycled.ETag));

        return await SubmitAsync(ownerId, grants, changes, cancellationToken);
    }

    public async Task<ErasedItems> EraseFoldersAsync(Guid ownerId, IReadOnlyList<Versioned<Folder>> folders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (folders.Count == 0)
        {
            return new ErasedItems(new VaultWriteResult(StoreWriteResult.Written, 0, []), []);
        }

        var grants = await ReadFolderGrantRowsAsync(ownerId, folders, cancellationToken);
        var changes = new List<VaultChange>();
        changes.AddRange(grants.Select(DeleteChange));
        changes.AddRange(folders.Select(folder => VaultChange.PurgeFolder(folder.Value, folder.ETag)));

        return await SubmitAsync(ownerId, grants, changes, cancellationToken);
    }

    private static VaultChange DeleteChange(GrantRows rows) => rows.ByRecipientETag is { } byRecipient
        ? VaultChange.DeleteShareGrant(rows.ByItem.Value, rows.ByItem.ETag, byRecipient)
        : VaultChange.DeleteShareGrantByItem(rows.ByItem.Value, rows.ByItem.ETag);

    private async Task<ErasedItems> SubmitAsync(Guid ownerId, IReadOnlyList<GrantRows> grantRows, List<VaultChange> changes, CancellationToken cancellationToken)
    {
        var write = await store.SubmitAsync(ownerId, changes, cancellationToken);
        var erased = new ErasedItems(write, [.. grantRows.Select(rows => rows.ByItem.Value)]);
        await RemoveInboxRowsAsync(erased.CommittedGrants);
        return erased;
    }

    private async Task<IReadOnlyList<GrantRows>> ReadFolderGrantRowsAsync(Guid ownerId, IReadOnlyList<Versioned<Folder>> folders, CancellationToken cancellationToken)
    {
        var perFolder = new ConcurrentDictionary<int, IReadOnlyList<GrantRows>>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = VaultLimits.FolderQueryParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, folders.Count), options, async (index, token) =>
        {
            perFolder[index] = await ReadGrantRowsAsync(ownerId, ShareItemKind.Folder, folders[index].Value.Id, token);
        });

        return [.. Enumerable.Range(0, folders.Count).SelectMany(index => perFolder[index])];
    }

    private async Task<IReadOnlyList<GrantRows>> ReadGrantRowsAsync(Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken)
    {
        var listed = await store.ListShareGrantsForItemAsync(ownerId, kind, itemId, GrantListLimit, cancellationToken);
        if (listed.Count == 0)
        {
            return [];
        }

        var rows = new ConcurrentDictionary<int, GrantRows>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = SharingLimits.ItemReadParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, listed.Count), options, async (index, token) =>
        {
            var grant = listed[index].Value;
            var byRecipient = await store.GetShareGrantByRecipientETagAsync(ownerId, kind, itemId, grant.RecipientId, token);
            rows[index] = new GrantRows(listed[index], byRecipient);
        });

        return [.. Enumerable.Range(0, listed.Count).Select(index => rows[index])];
    }

    private async Task RemoveInboxRowsAsync(IReadOnlyList<ShareGrant> committed)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = SharingLimits.ItemReadParallelism };
        await Parallel.ForEachAsync(committed, options, async (grant, _) =>
        {
            using var timeout = new CancellationTokenSource(SharingLimits.InboxCleanupTimeout, timeProvider);
            try
            {
                await inbox.RemoveAsync(grant.RecipientId, grant.OwnerId, grant.ItemKind, grant.ItemId, timeout.Token);
            }
            catch (Exception exception)
            {
                LogInboxCleanupFailed(logger, exception.GetType().Name);
            }
        });
    }

    private sealed record GrantRows(Versioned<ShareGrant> ByItem, string? ByRecipientETag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Share inbox cleanup after purge failed ({ExceptionType})")]
    private static partial void LogInboxCleanupFailed(ILogger logger, string exceptionType);
}
