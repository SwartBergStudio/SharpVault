// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Keys.Cryptography;
using SharpVault.Infrastructure.Encryption.KeyVault;

namespace SharpVault.UnitTests.Support.Encryption;

internal sealed class PerformanceKeyVaultHandler : HttpMessageHandler, IKeyVaultCryptographyClientFactory
{
    public const string VaultUri = "https://sharpvault-perf.vault.azure.net";
    public const string KeyName = "sharpvault-kek";
    public const string KeyVersion = "0123456789abcdef0123456789abcdef";

    private const string WrapKeySegment = "/wrapkey";
    private const string UnwrapKeySegment = "/unwrapkey";
    private const int RsaKeySize = 2048;

    private readonly RSA _rsa = RSA.Create(RsaKeySize);
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly KeyVaultCryptographyClientFactory _factory;

    public PerformanceKeyVaultHandler()
    {
        var options = KeyVaultCryptographyClientFactory.CreateOptions();
        options.Transport = new HttpClientTransport(new HttpClient(this));
        _factory = new KeyVaultCryptographyClientFactory(new StaticCredential(), options);
    }

    public static string VersionlessKeyId => $"{VaultUri}/keys/{KeyName}";

    public static string VersionedKeyId => $"{VaultUri}/keys/{KeyName}/{KeyVersion}";

    public bool AllowKeyRead { get; set; } = true;

    public int WrapKeyRequests => Count(WrapKeySegment, HttpMethod.Post);

    public int UnwrapKeyRequests => Count(UnwrapKeySegment, HttpMethod.Post);

    public int KeyReadRequests => _requests.Count(request => request.StartsWith("GET ", StringComparison.Ordinal));

    public int TotalRequests => _requests.Count;

    public CryptographyClient GetClient(Uri keyId) => _factory.GetClient(keyId);

    public void ResetCounts() => _requests.Clear();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            challenge.Headers.TryAddWithoutValidation(
                "WWW-Authenticate",
                "Bearer authorization=\"https://login.microsoftonline.com/perf-tenant\", resource=\"https://vault.azure.net\"");
            return Task.FromResult(challenge);
        }

        var path = request.RequestUri!.AbsolutePath.ToLowerInvariant();
        _requests.Enqueue($"{request.Method} {path}");

        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(AllowKeyRead ? Json(HttpStatusCode.OK, KeyDocument()) : new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        var body = JsonNode.Parse(request.Content!.ReadAsStream(cancellationToken))!;
        var value = Base64UrlDecode(body["value"]!.GetValue<string>());

        if (path.EndsWith(UnwrapKeySegment, StringComparison.Ordinal))
        {
            var key = _rsa.Decrypt(value, RSAEncryptionPadding.OaepSHA256);
            return Task.FromResult(Json(HttpStatusCode.OK, KeyResult(key)));
        }

        var wrapped = _rsa.Encrypt(value, RSAEncryptionPadding.OaepSHA256);
        return Task.FromResult(Json(HttpStatusCode.OK, KeyResult(wrapped)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rsa.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) => new(status)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private int Count(string segment, HttpMethod method) =>
        _requests.Count(request => request.StartsWith(method.Method + " ", StringComparison.Ordinal) && request.EndsWith(segment, StringComparison.Ordinal));

    private JsonObject KeyDocument()
    {
        var parameters = _rsa.ExportParameters(false);
        return new JsonObject
        {
            ["key"] = new JsonObject
            {
                ["kid"] = VersionedKeyId,
                ["kty"] = "RSA",
                ["key_ops"] = new JsonArray("wrapKey", "unwrapKey"),
                ["n"] = Base64UrlEncode(parameters.Modulus!),
                ["e"] = Base64UrlEncode(parameters.Exponent!),
            },
            ["attributes"] = new JsonObject { ["enabled"] = true },
        };
    }

    private static JsonObject KeyResult(byte[] value) => new()
    {
        ["kid"] = VersionedKeyId,
        ["value"] = Base64UrlEncode(value),
    };

    private sealed class StaticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("perf-access-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
