// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using SharpVault.Infrastructure.Encryption.KeyVault;

namespace SharpVault.UnitTests.Support.Encryption;

internal sealed class FakeKeyVault : IKeyVaultCryptographyClientFactory, IDisposable
{
    public const string VaultUri = "https://sharpvault-test.vault.azure.net";
    public const string KeyName = "sharpvault-kek";
    private const int RsaKeySizeBits = 2048;
    private const int BadRequestStatus = 400;
    private const int NotFoundStatus = 404;
    private const byte OverrideFill = 0xCD;
    private const string BadParameterMessage = "BadParameter";
    private const string KeyNotFoundMessage = "KeyNotFound";

    private readonly Lock _lock = new();
    private readonly List<(string Version, RSA Rsa)> _versions = [];
    private readonly ConcurrentQueue<string> _requestedKeyIds = new();
    private readonly ConcurrentQueue<byte[]> _wrapInputs = new();
    private readonly ConcurrentQueue<byte[]> _unwrapOutputs = new();
    private readonly ConcurrentQueue<CancellationToken> _wrapTokens = new();
    private readonly ConcurrentQueue<CancellationToken> _unwrapTokens = new();
    private readonly ConcurrentQueue<string> _wrapAlgorithms = new();
    private readonly ConcurrentQueue<string> _unwrapAlgorithms = new();
    private int _wrapCount;
    private int _unwrapCount;

    public FakeKeyVault()
    {
        Rotate();
    }

    public static string VersionlessKeyId => $"{VaultUri}/keys/{KeyName}";

    public int WrapCount => Volatile.Read(ref _wrapCount);

    public int UnwrapCount => Volatile.Read(ref _unwrapCount);

    public IReadOnlyList<string> RequestedKeyIds => [.. _requestedKeyIds];

    public IReadOnlyList<byte[]> WrapInputs => [.. _wrapInputs];

    public IReadOnlyList<byte[]> UnwrapOutputs => [.. _unwrapOutputs];

    public IReadOnlyList<CancellationToken> WrapTokens => [.. _wrapTokens];

    public IReadOnlyList<CancellationToken> UnwrapTokens => [.. _unwrapTokens];

    public IReadOnlyList<string> WrapAlgorithms => [.. _wrapAlgorithms];

    public IReadOnlyList<string> UnwrapAlgorithms => [.. _unwrapAlgorithms];

    public string? WrapKeyIdOverride { get; set; }

    public int? WrapLengthOverride { get; set; }

    public int? UnwrapLengthOverride { get; set; }

    public int? UnwrapFailureStatus { get; set; }

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

    public string CurrentKeyId => VersionedKeyId(CurrentVersion);

    public static string VersionedKeyId(string version) => $"{VersionlessKeyId}/{version}";

    public string Rotate()
    {
        var version = Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            _versions.Add((version, RSA.Create(RsaKeySizeBits)));
        }

        return version;
    }

    public CryptographyClient GetClient(Uri keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        _requestedKeyIds.Enqueue(keyId.AbsoluteUri);
        var segments = keyId.AbsolutePath.Trim('/').Split('/');
        var version = segments.Length > 2 ? segments[2] : null;
        return new FakeCryptographyClient(this, version);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, rsa) in _versions)
            {
                rsa.Dispose();
            }
        }
    }

    private WrapResult Wrap(string? version, KeyWrapAlgorithm algorithm, byte[] key, CancellationToken cancellationToken)
    {
        _wrapInputs.Enqueue(key);
        _wrapTokens.Enqueue(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _wrapCount);
        _wrapAlgorithms.Enqueue(algorithm.ToString());

        var (resolvedVersion, rsa) = Resolve(version);
        var encrypted = WrapLengthOverride is { } length
            ? Enumerable.Repeat(OverrideFill, length).ToArray()
            : rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256);
        var keyId = WrapKeyIdOverride ?? VersionedKeyId(resolvedVersion);
        return CryptographyModelFactory.WrapResult(keyId, encrypted, algorithm);
    }

    private UnwrapResult Unwrap(string? version, KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _unwrapCount);
        _unwrapTokens.Enqueue(cancellationToken);
        _unwrapAlgorithms.Enqueue(algorithm.ToString());

        if (UnwrapFailureStatus is { } status)
        {
            throw new RequestFailedException(status, BadParameterMessage);
        }

        var (resolvedVersion, rsa) = Resolve(version);
        byte[] key;
        try
        {
            key = UnwrapLengthOverride is { } length
                ? Enumerable.Repeat(OverrideFill, length).ToArray()
                : rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
        }
        catch (CryptographicException)
        {
            throw new RequestFailedException(BadRequestStatus, BadParameterMessage);
        }

        _unwrapOutputs.Enqueue(key);
        return CryptographyModelFactory.UnwrapResult(VersionedKeyId(resolvedVersion), key, algorithm);
    }

    private (string Version, RSA Rsa) Resolve(string? version)
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

        throw new RequestFailedException(NotFoundStatus, KeyNotFoundMessage);
    }

    private sealed class FakeCryptographyClient(FakeKeyVault vault, string? version) : CryptographyClient
    {
        public override Task<WrapResult> WrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] key, CancellationToken cancellationToken = default) =>
            Task.FromResult(vault.Wrap(version, algorithm, key, cancellationToken));

        public override Task<UnwrapResult> UnwrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(vault.Unwrap(version, algorithm, encryptedKey, cancellationToken));
    }
}
