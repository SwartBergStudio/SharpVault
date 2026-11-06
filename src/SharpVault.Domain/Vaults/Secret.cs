// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Encryption;

namespace SharpVault.Domain.Vaults;

public sealed class Secret
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public SecretType Type { get; set; }

    public Guid? FolderId { get; set; }

    public byte[] NameEnvelope { get; set; } = [];

    public byte[]? UsernameEnvelope { get; set; }

    public byte[]? UrlEnvelope { get; set; }

    public byte[]? TagsEnvelope { get; set; }

    public byte[]? ValueEnvelope { get; set; }

    public byte[]? NotesEnvelope { get; set; }

    public required WrappedDataKey DataKey { get; set; }

    public int MetadataKeyVersion { get; set; }

    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletionBatchId { get; set; }
}
