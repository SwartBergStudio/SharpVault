// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Dtos.Transfers;

namespace SharpVault.UI.Shared.Components.Transfers;

public static class TransferStatusText
{
    private const string StampFormat = "yyyy-MM-dd HH:mm";

    public const string SelfRequestNote = "You requested this transfer. A second administrator must approve it.";

    public static string State(TransferStateDto state) => state switch
    {
        TransferStateDto.PendingApproval => "Waiting for approval",
        TransferStateDto.Rejected => "Rejected",
        TransferStateDto.Expired => "Expired",
        TransferStateDto.Approved => "Approved",
        TransferStateDto.Staging => "Preparing",
        TransferStateDto.Blocked => "Blocked",
        TransferStateDto.Aborted => "Aborted",
        TransferStateDto.Releasing => "Moving items",
        TransferStateDto.Publishing => "Adding items",
        TransferStateDto.Reassigning => "Moving shares",
        TransferStateDto.Completed => "Completed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown transfer state."),
    };

    public static string Scope(TransferScopeKindDto scope) => scope switch
    {
        TransferScopeKindDto.AllItems => "All items",
        TransferScopeKindDto.SelectedFolders => "Selected folders",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown transfer scope."),
    };

    public static string BlockReason(TransferBlockReasonDto reason) => reason switch
    {
        TransferBlockReasonDto.RequesterNoLongerAdmin => "The requester is no longer an administrator.",
        TransferBlockReasonDto.ApproverNoLongerAdmin => "The approver is no longer an administrator.",
        TransferBlockReasonDto.SourceMissing => "The source user no longer exists.",
        TransferBlockReasonDto.SourceNotLocked => "The source user must be locked for this reason.",
        TransferBlockReasonDto.TargetMissing => "The target user no longer exists.",
        TransferBlockReasonDto.TargetLocked => "The target user is locked.",
        TransferBlockReasonDto.TargetFolderLimit => "The target would have more folders than the limit allows.",
        TransferBlockReasonDto.SourceVaultUnreadable => "The source vault could not be read.",
        TransferBlockReasonDto.TargetVaultUnreadable => "The target vault could not be read.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown block reason."),
    };

    public static string Stamp(DateTimeOffset value) => $"{value.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture)} UTC";

    public static bool IsInProgress(TransferStateDto state) => state is TransferStateDto.Approved
        or TransferStateDto.Staging
        or TransferStateDto.Blocked
        or TransferStateDto.Releasing
        or TransferStateDto.Publishing
        or TransferStateDto.Reassigning;
}
