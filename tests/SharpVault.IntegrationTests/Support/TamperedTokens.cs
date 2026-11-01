// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Support;

public static class TamperedTokens
{
    private const string AlgorithmHeader = "alg";
    private const string NoAlgorithm = "none";
    private const int ValidSegmentCount = 3;

    public static string AlgNone(TestUser user)
    {
        var segments = Split(TestTokens.Create(user));
        var header = new JsonObject
        {
            [AlgorithmHeader] = NoAlgorithm,
            ["typ"] = "JWT",
            ["kid"] = TestSigningKeys.KeyId,
        };
        return $"{Encode(header.ToJsonString())}.{segments[1]}.";
    }

    public static string AlgNoneKeepingSignature(TestUser user)
    {
        var segments = Split(TestTokens.Create(user));
        var header = new JsonObject
        {
            [AlgorithmHeader] = NoAlgorithm,
            ["typ"] = "JWT",
            ["kid"] = TestSigningKeys.KeyId,
        };
        return $"{Encode(header.ToJsonString())}.{segments[1]}.{segments[2]}";
    }

    public static string StrippedSignature(TestUser user)
    {
        var segments = Split(TestTokens.Create(user));
        return $"{segments[0]}.{segments[1]}.";
    }

    public static string GarbageSignature(TestUser user)
    {
        var segments = Split(TestTokens.Create(user));
        var signature = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(256));
        return $"{segments[0]}.{segments[1]}.{signature}";
    }

    public static string PayloadReplacedKeepingSignature(TestUser signedAs, Action<JsonObject> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var segments = Split(TestTokens.Create(signedAs));
        var payload = JsonNode.Parse(Base64Url.DecodeFromChars(segments[1]))!.AsObject();
        change(payload);
        return $"{segments[0]}.{Encode(payload.ToJsonString())}.{segments[2]}";
    }

    public static string WithExtraSegment(TestUser user) => TestTokens.Create(user) + "." + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string HmacSignedWithKeyMaterial(TestUser user, byte[] keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);

        var key = new SymmetricSecurityKey(keyMaterial) { KeyId = TestSigningKeys.KeyId };
        return TestTokens.For(user).SignedWith(key, SecurityAlgorithms.HmacSha256).Build();
    }

    public static IReadOnlyList<byte[]> PublicKeyEncodings()
    {
        var rsa = TestSigningKeys.Primary.Rsa;
        var spki = rsa.ExportSubjectPublicKeyInfo();
        return
        [
            rsa.ExportRSAPublicKey(),
            spki,
            Encoding.ASCII.GetBytes(rsa.ExportSubjectPublicKeyInfoPem()),
            Encoding.ASCII.GetBytes(rsa.ExportRSAPublicKeyPem()),
            Encoding.ASCII.GetBytes(Convert.ToBase64String(spki)),
        ];
    }

    public static string SignedWithFreshKey(TestUser user, bool reuseKeyId)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = reuseKeyId ? TestSigningKeys.KeyId : Guid.NewGuid().ToString("N") };
        return TestTokens.For(user).SignedWith(key, SecurityAlgorithms.RsaSha256).Build();
    }

    private static string[] Split(string token)
    {
        var segments = token.Split('.');
        Assert.Equal(ValidSegmentCount, segments.Length);
        return segments;
    }

    private static string Encode(string value) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(value));
}
