// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Vaults;

public sealed class SecretVersion
{
    public Guid Id { get; set; }

    public Guid SecretId { get; set; }

    public Guid OwnerId { get; set; }

    public byte[] ValueEnvelope { get; set; } = [];

    public long Revision { get; set; }

    public DateTimeOffset ReplacedAt { get; set; }
}
