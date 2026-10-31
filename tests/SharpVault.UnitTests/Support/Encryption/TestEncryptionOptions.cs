// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharpVault.Infrastructure.Encryption;

namespace SharpVault.UnitTests.Support.Encryption;

internal static class TestEncryptionOptions
{
    private const int RandomMaterialBytes = 36;

    public static string RandomMaterial() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(RandomMaterialBytes));

    public static IOptions<EncryptionOptions> Development(string? material = null) =>
        Options.Create(new EncryptionOptions { DevelopmentKeyMaterial = material ?? RandomMaterial() });

    public static IOptions<EncryptionOptions> KeyVault(string keyId) =>
        Options.Create(new EncryptionOptions { Provider = KeyWrapProviderKind.KeyVault, KeyVaultKeyId = keyId });
}
