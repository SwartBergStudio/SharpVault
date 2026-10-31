// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace SharpVault.IntegrationTests.Authentication;

public static class TestSigningKeys
{
    public const string KeyId = "sharpvault-test-signing-key";

    private const int KeySizeBits = 2048;

    public static RsaSecurityKey Primary { get; } = Create();

    public static RsaSecurityKey OtherWithSameKeyId { get; } = Create();

    private static RsaSecurityKey Create() => new(RSA.Create(KeySizeBits)) { KeyId = KeyId };
}
