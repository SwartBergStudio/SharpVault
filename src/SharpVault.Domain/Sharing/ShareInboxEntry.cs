// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Sharing;

public sealed class ShareInboxEntry
{
    public Guid RecipientId { get; set; }

    public Guid OwnerId { get; set; }

    public ShareItemKind ItemKind { get; set; }

    public Guid ItemId { get; set; }

    public DateTimeOffset SharedAt { get; set; }

    public Guid? FiledFolderId { get; set; }
}
