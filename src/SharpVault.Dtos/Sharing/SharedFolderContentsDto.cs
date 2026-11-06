// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.Dtos.Sharing;

public sealed class SharedFolderContentsDto
{
    public Guid FolderId { get; set; }

    public string? Name { get; set; }

    public bool IsDamaged { get; set; }

    public Guid OwnerId { get; set; }

    public string OwnerDisplayName { get; set; } = string.Empty;

    public SharePermissionDto Permission { get; set; }

    public List<SharedFolderSummaryDto> Path { get; set; } = [];

    public List<SharedFolderSummaryDto> SubFolders { get; set; } = [];

    public List<SecretDto> Items { get; set; } = [];

    public string? ContinuationToken { get; set; }
}
