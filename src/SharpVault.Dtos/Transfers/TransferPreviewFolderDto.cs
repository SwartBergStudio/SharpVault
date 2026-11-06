// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Transfers;

public sealed class TransferPreviewFolderDto
{
    public Guid FolderId { get; set; }

    public string Reference { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public int ItemCount { get; set; }
}
