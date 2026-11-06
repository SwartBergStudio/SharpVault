// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Components.Sharing;

public static class SharedItemStatusText
{
    public const string AccessEnded = "Your access to this item has ended.";
    public const string Filed = "Filed in your vault.";
    public const string Unfiled = "Removed from your folders.";
    public const string FilingFailed = "The item could not be filed. Try again.";
    public const string Shared = "Shared.";
    public const string PermissionChanged = "Access changed.";
    public const string Revoked = "Access removed.";
    public const string SearchFailed = "The search did not work. Try again.";
    public const string NoPeopleFound = "No matching people found.";
    public const string DamagedItem = "This item cannot be read.";
    public const string SharingNoticeOnDelete = "People it is shared with lose access until it is restored.";

    public static string RevokeWarning(string name) => $"{name} loses access at once. If they may have copied the value, change it.";
}
