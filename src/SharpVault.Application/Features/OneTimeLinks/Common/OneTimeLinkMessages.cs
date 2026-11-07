// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public static class OneTimeLinkMessages
{
    public const string OptionRangeFormat = "OneTimeLinks:{0} must be between {1} and {2}.";

    public const string SecretUnreadable = "This item could not be read, so a link cannot be made from it.";

    public const string LifetimeOutOfRange = "The lifetime must be between 5 minutes and 7 days.";

    public const string LifetimeTooLong = "The lifetime is longer than the maximum allowed.";

    public const string TooManyActiveLinks = "You have reached the limit of active one-time links. Revoke one before creating another.";

    public const string PayloadInvalid = "The payload is not a valid one-time link payload.";

    public const string InvalidContinuationToken = "ContinuationToken is not valid.";

    public const string InvalidNoticeId = "NoticeId is not valid.";
}
