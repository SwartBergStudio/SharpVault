// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpVault.IntegrationTests.Authentication;

public static class ForgedTokens
{
    private const int SigningKeyBytes = 32;
    private const int LifetimeHours = 1;

    public static string AlgNone()
    {
        var header = Encode("""{"alg":"none","typ":"JWT"}""");
        return $"{header}.{Encode(BuildPayload())}.";
    }

    public static string SignedWithUnknownKey()
    {
        var header = Encode("""{"alg":"HS256","typ":"JWT"}""");
        var signingInput = $"{header}.{Encode(BuildPayload())}";
        var key = RandomNumberGenerator.GetBytes(SigningKeyBytes);
        var signature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(signingInput));
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    private static string BuildPayload()
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["oid"] = Guid.NewGuid().ToString(),
            ["tid"] = TestTenant.Id.ToString(),
            ["aud"] = "api://sharpvault",
            ["iss"] = $"https://login.microsoftonline.com/{TestTenant.Id}/v2.0",
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(LifetimeHours).ToUnixTimeSeconds(),
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string Encode(string value) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(value));
}
