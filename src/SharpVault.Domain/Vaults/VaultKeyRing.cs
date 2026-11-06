// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Encryption;

namespace SharpVault.Domain.Vaults;

public sealed class VaultKeyRing
{
    public Guid OwnerId { get; set; }

    public required WrappedDataKey MetadataKey { get; set; }

    public int MetadataKeyVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
