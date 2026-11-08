// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SharpVault.Authentication.WebGateway;

public static class WebGatewaySignature
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    public const int SignatureLength = 43;

    private const string Version = "v1";
    private const char FieldSeparator = '\n';
    private const int HashLength = 32;
    private const long MinUnixSeconds = -62135596800;
    private const long MaxUnixSeconds = 253402300799;

    public static string Sign(string key, string purpose, string scope, long unixSeconds, string clientIp, string agentBase64Url)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!AreSafeFields(purpose, scope, clientIp, agentBase64Url))
        {
            throw new ArgumentException(WebGatewayMessages.FieldContainsNewLine);
        }

        var hash = ComputeHash(key, BuildMessage(purpose, scope, unixSeconds, clientIp, agentBase64Url));
        return Base64Url.EncodeToString(hash);
    }

    public static bool Verify(
        string key,
        string purpose,
        string scope,
        long unixSeconds,
        string clientIp,
        string agentBase64Url,
        string signature,
        DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(key)
            || signature is null
            || signature.Length != SignatureLength
            || !AreSafeFields(purpose, scope, clientIp, agentBase64Url)
            || !IsWithinWindow(unixSeconds, now)
            || !TryDecodeSignature(signature, out var provided))
        {
            return false;
        }

        var expected = ComputeHash(key, BuildMessage(purpose, scope, unixSeconds, clientIp, agentBase64Url));
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static string BuildMessage(string purpose, string scope, long unixSeconds, string clientIp, string agentBase64Url) =>
        string.Join(
            FieldSeparator,
            Version,
            purpose,
            scope,
            unixSeconds.ToString(CultureInfo.InvariantCulture),
            clientIp,
            agentBase64Url);

    private static byte[] ComputeHash(string key, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        try
        {
            return HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(message));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static bool AreSafeFields(params string?[] fields) =>
        fields.All(field => field is not null && !field.Contains(FieldSeparator));

    private static bool IsWithinWindow(long unixSeconds, DateTimeOffset now)
    {
        if (unixSeconds is < MinUnixSeconds or > MaxUnixSeconds)
        {
            return false;
        }

        var issued = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        return (issued - now).Duration() <= Window;
    }

    private static bool TryDecodeSignature(string signature, out byte[] provided)
    {
        provided = new byte[HashLength];
        var status = Base64Url.DecodeFromChars(signature.AsSpan(), provided, out _, out var written);
        return status == OperationStatus.Done && written == HashLength;
    }
}
