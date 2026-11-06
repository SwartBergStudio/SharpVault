// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.Dtos.Sharing;

public sealed class SharedSecretDto
{
    public SecretDto Item { get; set; } = new();

    public Guid OwnerId { get; set; }

    public string OwnerDisplayName { get; set; } = string.Empty;

    public SharePermissionDto Permission { get; set; }
}
