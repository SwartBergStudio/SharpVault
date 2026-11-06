// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;

namespace SharpVault.Dtos.Transfers;

public sealed class OwnedShareDto
{
    public SharedItemKindDto Kind { get; set; }

    public Guid ItemId { get; set; }

    public string? Name { get; set; }

    public bool IsDamaged { get; set; }

    public Guid RecipientId { get; set; }

    public string RecipientDisplayName { get; set; } = string.Empty;

    public SharePermissionDto Permission { get; set; }

    public DateTimeOffset GrantedAt { get; set; }
}
