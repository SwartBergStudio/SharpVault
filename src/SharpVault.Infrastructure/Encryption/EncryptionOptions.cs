// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Encryption;

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    public KeyWrapProviderKind? Provider { get; set; }

    public string? KeyVaultKeyId { get; set; }

    public string? DevelopmentKeyMaterial { get; set; }
}
