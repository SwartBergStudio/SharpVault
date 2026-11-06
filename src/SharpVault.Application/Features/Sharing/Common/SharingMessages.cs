// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Sharing.Common;

public static class SharingMessages
{
    public const string RecipientUnavailable = "This person cannot receive a share.";
    public const string TooManyRecipients = "This item is already shared with the maximum number of people.";
    public const string InvalidContinuationToken = "ContinuationToken is not valid.";
    public const string UnknownOwner = "Unknown owner";
    public const string OnlyOwnerGrantsManage = "Only the owner can give this level of access.";
}
