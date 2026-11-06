// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferMessages
{
    public const string RequesterIsParty = "You cannot transfer to or from your own account.";

    public const string SourceIsTarget = "The source and the target must be different users.";

    public const string SourceMustBeLocked = "The source user must be locked for this reason.";

    public const string TargetLocked = "The target user must be active.";

    public const string SecondAdminRequired = "A second administrator, other than the requester, the source and the target, must be active.";

    public const string FolderNotSelectable = "Choose top-level folders that exist in the source vault.";

    public const string TargetFolderLimit = "The target vault would exceed the folder limit.";

    public const string TransferChainNotAllowed = "The target user is the source of an open transfer, or the source user is the target of one.";

    public const string OpenTransferExists = "This user already has an open transfer.";

    public const string RequestExpired = "This request has expired. Create a new request.";

    public const string RequesterNoLongerAdmin = "The requester is no longer an active administrator.";

    public const string ApproverNoLongerAdmin = "The approver is no longer an active administrator.";

    public const string SourceMissing = "The source user no longer exists.";

    public const string TargetMissing = "The target user no longer exists.";

    public const string SourceVaultUnreadable = "The source vault cannot be read with the current keys.";

    public const string TargetVaultUnreadable = "The target vault cannot be read with the current keys.";

    public const string FolderSelectionInvalid = "Choose between 1 and 100 different top-level folders, or no folders for all items.";

    public const string StaleToken = "This transfer changed. It has been reloaded.";

    public const string InvalidContinuationToken = "ContinuationToken is not valid.";
}
