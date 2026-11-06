// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Vault;

public sealed class SecretVersionDto
{
    public Guid VersionId { get; set; }

    public DateTimeOffset ReplacedAt { get; set; }

    public long Revision { get; set; }
}
