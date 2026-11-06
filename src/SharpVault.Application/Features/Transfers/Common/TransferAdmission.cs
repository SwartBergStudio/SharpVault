// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Common;

public sealed class TransferAdmission(IVaultStore vaults)
{
    public async Task<TransferSelectionCheck> CheckSelectionAsync(
        Guid sourceUserId,
        Guid targetUserId,
        TransferScopeKind scope,
        IReadOnlyList<Guid> folderIds,
        CancellationToken cancellationToken)
    {
        var inventory = await TransferInventoryReader.ReadAsync(vaults, sourceUserId, int.MaxValue, cancellationToken);
        var selection = new TransferJob { ScopeKind = scope, SelectedFolderIds = folderIds };
        var scoped = TransferScope.Build(selection, inventory.Rows, Guid.Empty);
        if (scope == TransferScopeKind.SelectedFolders && !TransferFolderSelection.AreTopLevelActive(inventory.Rows, folderIds))
        {
            return new TransferSelectionCheck(TransferMessages.FolderNotSelectable, scoped);
        }

        var targetFolders = (await vaults.ListFoldersAsync(targetUserId, cancellationToken)).Count;
        var problem = TransferRules.FolderLimitAllows(targetFolders, scoped.InScopeActiveFolderCount)
            ? null
            : TransferMessages.TargetFolderLimit;
        return new TransferSelectionCheck(problem, scoped);
    }
}
