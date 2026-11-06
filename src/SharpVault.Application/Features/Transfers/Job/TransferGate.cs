// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferGate(IUserStore users, IVaultStore vaults)
{
    public async Task<TransferBlockReason?> CheckAsync(TransferJob job, bool checkFolderLimit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!IsActiveAdmin(await users.GetAsync(job.RequestedBy, cancellationToken)))
        {
            return TransferBlockReason.RequesterNoLongerAdmin;
        }

        var approver = job.DecidedBy is { } approverId ? await users.GetAsync(approverId, cancellationToken) : null;
        if (!IsActiveAdmin(approver))
        {
            return TransferBlockReason.ApproverNoLongerAdmin;
        }

        var source = await users.GetAsync(job.SourceUserId, cancellationToken);
        if (source is null)
        {
            return TransferBlockReason.SourceMissing;
        }

        if (TransferRules.RequiresLockedSource(job.Category) && source.Value.Status != UserStatus.Locked)
        {
            return TransferBlockReason.SourceNotLocked;
        }

        var target = await users.GetAsync(job.TargetUserId, cancellationToken);
        if (target is null)
        {
            return TransferBlockReason.TargetMissing;
        }

        if (target.Value.Status != UserStatus.Active)
        {
            return TransferBlockReason.TargetLocked;
        }

        if (checkFolderLimit && !await FolderLimitAllowsAsync(job, cancellationToken))
        {
            return TransferBlockReason.TargetFolderLimit;
        }

        return null;
    }

    private static bool IsActiveAdmin(Versioned<User>? user) =>
        user is { Value: { Status: UserStatus.Active, IsAdmin: true } };

    private async Task<bool> FolderLimitAllowsAsync(TransferJob job, CancellationToken cancellationToken)
    {
        var inventory = await TransferInventoryReader.ReadAsync(vaults, job.SourceUserId, int.MaxValue, cancellationToken);
        var targetFolders = (await vaults.ListFoldersAsync(job.TargetUserId, cancellationToken)).Count;
        var scope = TransferScope.Build(job, inventory.Rows, TransferRootFolder.IdOf(job));

        return TransferRules.FolderLimitAllows(targetFolders, scope.InScopeActiveFolderCount);
    }
}
