// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferFolderSelection
{
    public static bool IsValid(TransferScopeKind scope, IReadOnlyList<Guid>? folderIds) => scope switch
    {
        TransferScopeKind.AllItems => folderIds is null || folderIds.Count == 0,
        TransferScopeKind.SelectedFolders => folderIds is { Count: >= 1 and <= TransferLimits.MaxSelectedFolders }
            && folderIds.All(id => id != Guid.Empty)
            && folderIds.Distinct().Count() == folderIds.Count,
        _ => false,
    };

    public static IReadOnlyList<Guid> Normalize(TransferScopeKind scope, IReadOnlyList<Guid>? folderIds) =>
        scope == TransferScopeKind.SelectedFolders && folderIds is not null ? [.. folderIds.Distinct()] : [];

    public static bool AreTopLevelActive(IReadOnlyList<VaultInventoryRow> rows, IReadOnlyList<Guid> folderIds)
    {
        var topLevel = rows
            .Where(row => row.Kind == VaultItemKind.ActiveFolder && row.ContainerId is null)
            .Select(row => row.ItemId)
            .ToHashSet();

        return folderIds.All(topLevel.Contains);
    }
}
