// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;
using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferRules
{
    public static bool RequiresLockedSource(ReasonCategory category) =>
        category is ReasonCategory.LeftCompany or ReasonCategory.Dismissed or ReasonCategory.Deceased;

    public static bool CanAbort(TransferState state) =>
        state is TransferState.Approved or TransferState.Staging or TransferState.Blocked;

    public static bool IsFinal(TransferState state) =>
        state is TransferState.Rejected or TransferState.Expired or TransferState.Aborted or TransferState.Completed;

    public static bool FolderLimitAllows(int targetActiveFolders, int inScopeActiveFolders) =>
        targetActiveFolders + inScopeActiveFolders + 1 <= VaultLimits.MaxFoldersPerVault;
}
