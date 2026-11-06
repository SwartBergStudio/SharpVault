// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Sharing;

public sealed class ShareGrantDto
{
    public Guid RecipientId { get; set; }

    public string RecipientDisplayName { get; set; } = string.Empty;

    public string? RecipientEmail { get; set; }

    public SharePermissionDto Permission { get; set; }

    public string GrantedByDisplayName { get; set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public string ConcurrencyToken { get; set; } = string.Empty;

    public bool CanChange { get; set; }
}
