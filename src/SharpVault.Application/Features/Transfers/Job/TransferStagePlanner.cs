// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Features.Transfers.Job;

public static class TransferStagePlanner
{
    public static TransferStagePlan Build(
        TransferScope scope,
        IReadOnlyList<VaultInventoryRow> rows,
        IReadOnlyList<StagedItemRef> index,
        Guid rootFolderId,
        bool removeMissing)
    {
        var secretRows = rows.Where(row => TransferScope.IsSecret(row.Kind)).Select(row => row.ItemId).ToHashSet();
        var folderRows = rows.Where(TransferScope.IsFolder).Select(row => row.ItemId).ToHashSet();
        var inScopeSecretIds = scope.InScope.Where(row => TransferScope.IsSecret(row.Kind)).Select(row => row.ItemId).ToHashSet();

        var stagedSecrets = index.Where(reference => reference.Kind == StagedItemKind.Secret).ToDictionary(reference => reference.ItemId);
        var skipped = index.Where(reference => reference.Kind == StagedItemKind.Skipped).ToDictionary(reference => reference.ItemId);
        var stagedFolders = index
            .Where(reference => reference.Kind == StagedItemKind.Folder && reference.ItemId != rootFolderId)
            .ToDictionary(reference => reference.ItemId);

        var secretsToStage = scope.InScope
            .Where(row => TransferScope.IsSecret(row.Kind) && NeedsSecret(row, stagedSecrets, skipped))
            .ToList();
        var foldersToStage = scope.InScope
            .Where(row => TransferScope.IsFolder(row) && NeedsFolder(row, stagedFolders))
            .ToList();
        var stageRoot = scope.InScope.Count > 0 && !index.Any(reference => reference.Kind == StagedItemKind.Folder && reference.ItemId == rootFolderId);

        var removals = RemovalsFor(index, secretRows, folderRows, inScopeSecretIds, scope, rootFolderId, removeMissing);

        return new TransferStagePlan(secretsToStage, foldersToStage, stageRoot, removals);
    }

    private static bool NeedsSecret(
        VaultInventoryRow row,
        Dictionary<Guid, StagedItemRef> stagedSecrets,
        Dictionary<Guid, StagedItemRef> skipped)
    {
        if (stagedSecrets.TryGetValue(row.ItemId, out var staged))
        {
            return staged.SourceETag != row.ETag;
        }

        return !skipped.TryGetValue(row.ItemId, out var skip) || skip.SourceETag != row.ETag;
    }

    private static bool NeedsFolder(VaultInventoryRow row, Dictionary<Guid, StagedItemRef> stagedFolders) =>
        !stagedFolders.TryGetValue(row.ItemId, out var staged) || staged.SourceETag != row.ETag;

    private static List<StagedItemRef> RemovalsFor(
        IReadOnlyList<StagedItemRef> index,
        HashSet<Guid> secretRows,
        HashSet<Guid> folderRows,
        HashSet<Guid> inScopeSecretIds,
        TransferScope scope,
        Guid rootFolderId,
        bool removeMissing)
    {
        var removedSecrets = new HashSet<Guid>();
        var removals = new List<StagedItemRef>();

        foreach (var reference in index.Where(reference => reference.Kind is StagedItemKind.Secret or StagedItemKind.Skipped))
        {
            var removed = secretRows.Contains(reference.ItemId)
                ? !inScopeSecretIds.Contains(reference.ItemId)
                : removeMissing;
            if (removed)
            {
                removedSecrets.Add(reference.ItemId);
                removals.Add(reference);
            }
        }

        var stagedSecretIds = index.Where(reference => reference.Kind == StagedItemKind.Secret).Select(reference => reference.ItemId).ToHashSet();
        foreach (var reference in index.Where(reference => reference.Kind == StagedItemKind.Version && reference.SecretId is not null))
        {
            var secretId = reference.SecretId!.Value;
            if (removedSecrets.Contains(secretId) || !stagedSecretIds.Contains(secretId))
            {
                removals.Add(reference);
            }
        }

        foreach (var reference in index.Where(reference => reference.Kind == StagedItemKind.Folder && reference.ItemId != rootFolderId))
        {
            var removed = folderRows.Contains(reference.ItemId)
                ? !scope.Contains(reference.ItemId)
                : removeMissing;
            if (removed)
            {
                removals.Add(reference);
            }
        }

        if (removeMissing && scope.InScope.Count == 0)
        {
            removals.AddRange(index.Where(reference => reference.Kind == StagedItemKind.Folder && reference.ItemId == rootFolderId));
        }

        return removals;
    }
}
