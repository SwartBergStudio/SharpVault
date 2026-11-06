// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Sharing;

public sealed class ShareGrant
{
    public Guid OwnerId { get; set; }

    public ShareItemKind ItemKind { get; set; }

    public Guid ItemId { get; set; }

    public Guid RecipientId { get; set; }

    public SharePermission Permission { get; set; }

    public string RecipientDisplayName { get; set; } = string.Empty;

    public string? RecipientEmail { get; set; }

    public Guid GrantedById { get; set; }

    public string GrantedByDisplayName { get; set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }
}
