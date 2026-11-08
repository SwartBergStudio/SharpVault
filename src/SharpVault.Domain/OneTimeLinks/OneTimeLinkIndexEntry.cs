// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.OneTimeLinks;

public sealed class OneTimeLinkIndexEntry
{
    public Guid LinkId { get; set; }

    public Guid CreatorId { get; set; }

    public Guid OwnerId { get; set; }

    public Guid SecretId { get; set; }

    public byte[] NameEnvelope { get; set; } = [];

    public int MetadataKeyVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
