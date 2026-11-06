// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Users.Common;

public static class UserAdminMessages
{
    public static readonly string ReasonTooShort = $"The reason must be at least {ReasonRules.MinLength} characters.";

    public static readonly string ReasonTooLong = $"The reason must be at most {ReasonRules.MaxLength} characters.";

    public const string ReasonNotAllowed = "The reason contains characters that are not allowed.";

    public const string CategoryInvalid = "Choose a reason category.";

    public const string CannotLockSelf = "You cannot lock your own account.";

    public const string AlreadyLocked = "This user is already locked.";

    public const string AlreadyActive = "This user is already active.";

    public const string ReceivedSharesNotRevoked = "The user is locked, but some received shares could not be revoked. Lock the user again to finish.";
}
