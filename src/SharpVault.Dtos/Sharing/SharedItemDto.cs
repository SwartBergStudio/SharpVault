// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.Dtos.Sharing;

public sealed class SharedItemDto
{
    public SharedItemKindDto Kind { get; set; }

    public Guid ItemId { get; set; }

    public Guid OwnerId { get; set; }

    public string OwnerDisplayName { get; set; } = string.Empty;

    public string? Name { get; set; }

    public SecretTypeDto? Type { get; set; }

    public string? Username { get; set; }

    public SharePermissionDto Permission { get; set; }

    public DateTimeOffset SharedAt { get; set; }

    public Guid? FiledFolderId { get; set; }

    public bool IsDamaged { get; set; }
}
