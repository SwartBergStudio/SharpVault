// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferScope
{
    private readonly HashSet<Guid> _folderIds;
    private readonly Guid _rootFolderId;

    private TransferScope(IReadOnlyList<VaultInventoryRow> rows, HashSet<Guid> folderIds, Guid rootFolderId)
    {
        InScope = rows;
        _folderIds = folderIds;
        _rootFolderId = rootFolderId;
    }

    public IReadOnlyList<VaultInventoryRow> InScope { get; }

    public int InScopeActiveFolderCount => InScope.Count(row => row.Kind == VaultItemKind.ActiveFolder);

    public void ApplyCountsTo(TransferJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.ItemCount = InScope.Count(row => row.Kind == VaultItemKind.ActiveSecret);
        job.RecycledItemCount = InScope.Count(row => row.Kind == VaultItemKind.RecycledSecret);
        job.FolderCount = InScope.Count(IsFolder);
        job.VersionCount = InScope.Count(row => row.Kind == VaultItemKind.Version);
    }

    public static TransferScope Build(TransferJob job, IReadOnlyList<VaultInventoryRow> rows, Guid rootFolderId)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(rows);

        var folderIds = job.ScopeKind == TransferScopeKind.SelectedFolders
            ? FolderClosure(job.SelectedFolderIds, rows)
            : rows.Where(IsFolder).Select(row => row.ItemId).ToHashSet();

        var secretIds = rows.Where(row => IsSecret(row.Kind) && row.ContainerId is { } folder && folderIds.Contains(folder))
            .Select(row => row.ItemId)
            .ToHashSet();

        var secretRowIds = rows
            .Where(row => IsSecret(row.Kind))
            .Select(row => row.ItemId)
            .ToHashSet();

        var inScope = rows
            .Where(row => IsInScope(row, job.ScopeKind, folderIds, secretIds, secretRowIds))
            .ToList();

        return new TransferScope(inScope, folderIds, rootFolderId);
    }

    public static IReadOnlyList<VaultInventoryRow> FoldersDeepestFirst(IReadOnlyList<VaultInventoryRow> folders, IReadOnlyList<VaultInventoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(rows);

        var parents = rows.Where(IsFolder).ToDictionary(row => row.ItemId, row => row.ContainerId);
        var limit = rows.Count;

        return folders.OrderByDescending(folder => DepthOf(folder.ItemId, parents, limit)).ToList();
    }

    public Guid? MapFolderId(Guid? folderId) =>
        folderId is { } id && _folderIds.Contains(id) ? id : _rootFolderId;

    public bool Contains(Guid folderId) => _folderIds.Contains(folderId);

    public static bool IsFolder(VaultInventoryRow row) =>
        row.Kind is VaultItemKind.ActiveFolder or VaultItemKind.RecycledFolder;

    public static bool IsSecret(VaultItemKind kind) =>
        kind is VaultItemKind.ActiveSecret or VaultItemKind.RecycledSecret;

    private static int DepthOf(Guid folderId, Dictionary<Guid, Guid?> parents, int limit)
    {
        var depth = 0;
        Guid? cursor = folderId;
        while (cursor is { } current && parents.TryGetValue(current, out var parent) && depth <= limit)
        {
            depth++;
            cursor = parent;
        }

        return depth;
    }

    private static HashSet<Guid> FolderClosure(IReadOnlyList<Guid> selected, IReadOnlyList<VaultInventoryRow> rows)
    {
        var folders = rows.Where(IsFolder).ToList();
        var closure = selected.Where(id => folders.Any(folder => folder.ItemId == id)).ToHashSet();
        var added = true;
        while (added)
        {
            added = false;
            foreach (var folder in folders)
            {
                if (folder.ContainerId is { } parent && closure.Contains(parent) && closure.Add(folder.ItemId))
                {
                    added = true;
                }
            }
        }

        return closure;
    }

    private static bool IsInScope(
        VaultInventoryRow row,
        TransferScopeKind kind,
        HashSet<Guid> folderIds,
        HashSet<Guid> secretIds,
        HashSet<Guid> secretRowIds)
    {
        if (row.Kind == VaultItemKind.Version)
        {
            return row.ContainerId is { } secret
                && secretRowIds.Contains(secret)
                && (kind == TransferScopeKind.AllItems || secretIds.Contains(secret));
        }

        return kind == TransferScopeKind.AllItems
            || (IsFolder(row) && folderIds.Contains(row.ItemId))
            || (IsSecret(row.Kind) && secretIds.Contains(row.ItemId));
    }
}
