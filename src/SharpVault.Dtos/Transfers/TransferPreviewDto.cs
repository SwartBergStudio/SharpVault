// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Users;

namespace SharpVault.Dtos.Transfers;

public sealed class TransferPreviewDto
{
    public Guid SourceUserId { get; set; }

    public string SourceDisplayName { get; set; } = string.Empty;

    public UserStatusDto SourceStatus { get; set; }

    public int ItemCount { get; set; }

    public int RecycledItemCount { get; set; }

    public int FolderCount { get; set; }

    public int VersionCount { get; set; }

    public bool CountsAreCapped { get; set; }

    public List<TransferPreviewFolderDto> TopLevelFolders { get; set; } = [];

    public bool SecondAdminAvailable { get; set; }

    public bool HasOpenTransfer { get; set; }
}
