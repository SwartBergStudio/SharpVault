// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core.Pipeline;
using SharpVault.Infrastructure.Encryption.KeyVault;

namespace SharpVault.UnitTests.Support.Encryption;

internal sealed class FakeKeyVaultHttpHandler : HttpMessageHandler
{
    public const string VaultHost = "sharpvault-test.vault.azure.net";
    public const string KeyName = "sharpvault-kek";
    private const int RsaKeySizeBits = 2048;
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string KeysSegment = "keys";
    private const string WrapKeyOperation = "wrapKey";
    private const string UnwrapKeyOperation = "unwrapKey";
    private const string JsonContentType = "application/json";

    private readonly Lock _lock = new();
    private readonly List<(string Version, RSA Rsa)> _versions = [];
    private readonly ConcurrentQueue<KeyVaultHttpRequest> _requests = new();

    public FakeKeyVaultHttpHandler()
    {
        Rotate();
    }

    public bool AllowKeyRead { get; set; } = true;

    public int? UnwrapStatusOverride { get; set; }

    public IReadOnlyList<KeyVaultHttpRequest> Requests => [.. _requests];

    public IEnumerable<KeyVaultHttpRequest> AuthorizedRequests => Requests.Where(request => request.Authorized);

    public string CurrentVersion
    {
        get
        {
            lock (_lock)
            {
                return _versions[^1].Version;
            }
        }
    }

    public string CurrentKeyId => FakeKeyVault.VersionedKeyId(CurrentVersion);

    public string Rotate()
    {
        var version = Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            _versions.Add((version, RSA.Create(RsaKeySizeBits)));
        }

        return version;
    }

    public KeyVaultCryptographyClientFactory CreateFactory(bool contentLogging = false)
    {
        var options = KeyVaultCryptographyClientFactory.CreateOptions();
        options.Diagnostics.IsLoggingContentEnabled = contentLogging;
        options.Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false));
        return new KeyVaultCryptographyClientFactory(new StaticTokenCredential(), options);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var authorized = request.Headers.Authorization is { Scheme: "Bearer", Parameter: StaticTokenCredential.Token };
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var segments = uri.AbsolutePath.Trim('/').Split('/');

        if (!authorized)
        {
            Record(request, uri, false, null, null, null);
            return Challenge();
        }

        var operation = segments[^1];
        var isWrap = string.Equals(operation, WrapKeyOperation, StringComparison.OrdinalIgnoreCase);
        var isUnwrap = string.Equals(operation, UnwrapKeyOperation, StringComparison.OrdinalIgnoreCase);
        var hasOperation = isWrap || isUnwrap;
        var name = segments.Length > 1 ? segments[1] : string.Empty;
        var version = segments.Length > (hasOperation ? 3 : 2) ? segments[2] : null;
        var authority = uri.GetLeftPart(UriPartial.Authority);

        if (segments[0] != KeysSegment || !string.Equals(name, KeyName, StringComparison.OrdinalIgnoreCase))
        {
            Record(request, uri, true, null, null, null);
            return Json(HttpStatusCode.NotFound, Error("KeyNotFound"));
        }

        if (!hasOperation && request.Method == HttpMethod.Get)
        {
            Record(request, uri, true, null, null, null);
            return AllowKeyRead ? ReadKey(authority, version) : Json(HttpStatusCode.Forbidden, Error("Forbidden"));
        }

        if (hasOperation && request.Method == HttpMethod.Post)
        {
            return Crypto(request, uri, authority, version, isWrap, body);
        }

        Record(request, uri, true, null, null, null);
        return Json(HttpStatusCode.NotFound, Error("NotFound"));
    }

    private static HttpResponseMessage Challenge()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, JsonContentType),
        };
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
            "Bearer",
            $"authorization=\"https://login.microsoftonline.com/{TenantId}\", resource=\"https://vault.azure.net\""));
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, JsonContentType) };

    private static string Error(string code) => JsonSerializer.Serialize(new { error = new { code, message = code } });

    private HttpResponseMessage ReadKey(string authority, string? version)
    {
        var (resolvedVersion, rsa) = Resolve(version);
        if (resolvedVersion is null || rsa is null)
        {
            return Json(HttpStatusCode.NotFound, Error("KeyNotFound"));
        }

        var parameters = rsa.ExportParameters(false);
        var key = new
        {
            kid = KeyId(authority, resolvedVersion),
            kty = "RSA",
            key_ops = new[] { WrapKeyOperation, UnwrapKeyOperation },
            n = KeyMaterialLeakAssert.ToBase64Url(parameters.Modulus!),
            e = KeyMaterialLeakAssert.ToBase64Url(parameters.Exponent!),
        };
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { key, attributes = new { enabled = true } }));
    }

    private HttpResponseMessage Crypto(HttpRequestMessage request, Uri uri, string authority, string? version, bool isWrap, string body)
    {
        using var document = JsonDocument.Parse(body);
        var algorithm = document.RootElement.GetProperty("alg").GetString();
        var requestValue = document.RootElement.GetProperty("value").GetString()!;
        var (resolvedVersion, rsa) = Resolve(version);

        if (resolvedVersion is null || rsa is null)
        {
            Record(request, uri, true, algorithm, requestValue, null);
            return Json(HttpStatusCode.NotFound, Error("KeyNotFound"));
        }

        if (!isWrap && UnwrapStatusOverride is { } status)
        {
            Record(request, uri, true, algorithm, requestValue, null);
            return Json((HttpStatusCode)status, Error("Injected"));
        }

        var input = KeyMaterialLeakAssert.FromBase64Url(requestValue);
        byte[] output;
        try
        {
            output = isWrap
                ? rsa.Encrypt(input, RSAEncryptionPadding.OaepSHA256)
                : rsa.Decrypt(input, RSAEncryptionPadding.OaepSHA256);
        }
        catch (CryptographicException)
        {
            Record(request, uri, true, algorithm, requestValue, null);
            return Json(HttpStatusCode.BadRequest, Error("BadParameter"));
        }

        var responseValue = KeyMaterialLeakAssert.ToBase64Url(output);
        Record(request, uri, true, algorithm, requestValue, responseValue);
        return Json(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { kid = KeyId(authority, resolvedVersion), value = responseValue }));
    }

    private (string? Version, RSA? Rsa) Resolve(string? version)
    {
        lock (_lock)
        {
            if (version is null)
            {
                return _versions[^1];
            }

            foreach (var entry in _versions)
            {
                if (string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }

        return (null, null);
    }

    private static string KeyId(string authority, string version) => $"{authority}/{KeysSegment}/{KeyName}/{version}";

    private void Record(HttpRequestMessage request, Uri uri, bool authorized, string? algorithm, string? requestValue, string? responseValue) =>
        _requests.Enqueue(new KeyVaultHttpRequest(
            request.Method.Method,
            uri.Scheme,
            uri.Host,
            uri.AbsolutePath,
            authorized,
            algorithm,
            requestValue,
            responseValue));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                foreach (var (_, rsa) in _versions)
                {
                    rsa.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
