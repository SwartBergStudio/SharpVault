// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Vaults;

public sealed class Folder
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? ParentId { get; set; }

    public byte[] NameEnvelope { get; set; } = [];

    public int MetadataKeyVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletionBatchId { get; set; }
}
