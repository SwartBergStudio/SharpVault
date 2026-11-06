// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Sharing;

public sealed class SharedFolderSummaryDto
{
    public Guid FolderId { get; set; }

    public string? Name { get; set; }

    public bool IsDamaged { get; set; }
}
