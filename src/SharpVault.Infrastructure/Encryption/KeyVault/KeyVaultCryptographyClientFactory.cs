// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using Azure.Core;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace SharpVault.Infrastructure.Encryption.KeyVault;

public sealed class KeyVaultCryptographyClientFactory(TokenCredential credential, CryptographyClientOptions options) : IKeyVaultCryptographyClientFactory
{
    public const int MaxCachedClients = 64;
    public const int MaxRetries = 3;
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, CryptographyClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public CryptographyClient GetClient(Uri keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        var key = keyId.AbsoluteUri;
        if (_clients.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var client = new CryptographyClient(keyId, credential, options);
        return _clients.Count < MaxCachedClients ? _clients.GetOrAdd(key, client) : client;
    }

    public static CryptographyClientOptions CreateOptions()
    {
        var options = new CryptographyClientOptions();
        options.Diagnostics.IsLoggingContentEnabled = false;
        options.Retry.MaxRetries = MaxRetries;
        options.Retry.NetworkTimeout = NetworkTimeout;
        return options;
    }
}
