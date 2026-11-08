// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Components.OneTimeLinks;

public static class OneTimeLinkText
{
    public const string UnreadableNameText = "Unreadable name";

    public static string NameOf(string? secretName) =>
        string.IsNullOrWhiteSpace(secretName) ? UnreadableNameText : secretName;
}
