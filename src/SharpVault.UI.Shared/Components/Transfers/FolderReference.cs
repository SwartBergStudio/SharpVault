// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Components.Transfers;

public static class FolderReference
{
    private const string GuidFormat = "N";
    private const int ReferenceLength = 8;

    public static string Of(Guid folderId)
    {
        var text = folderId.ToString(GuidFormat);
        return text[^ReferenceLength..];
    }
}
