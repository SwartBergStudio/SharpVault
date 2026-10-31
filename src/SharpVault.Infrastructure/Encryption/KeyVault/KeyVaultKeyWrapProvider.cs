// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;

namespace SharpVault.Infrastructure.Encryption.KeyVault;

public sealed class KeyVaultKeyWrapProvider : IKeyWrapProvider
{
    public const string Algorithm = "RSA-OAEP-256";

    private const int Rsa2048WrappedLength = 256;
    private const int Rsa3072WrappedLength = 384;
    private const int Rsa4096WrappedLength = 512;
    private const int BadRequestStatus = 400;

    private readonly IKeyVaultCryptographyClientFactory _clientFactory;
    private readonly KeyVaultKeyIdentifier _configuredKey;
    private readonly CryptographyClient _wrapClient;

    public KeyVaultKeyWrapProvider(IKeyVaultCryptographyClientFactory clientFactory, IOptions<EncryptionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(options);

        if (!KeyVaultKeyIds.TryParseConfigured(options.Value.KeyVaultKeyId, out var configured))
        {
            throw new InvalidOperationException(EncryptionMessages.KeyVaultKeyIdInvalid);
        }

        _clientFactory = clientFactory;
        _configuredKey = configured.Value;
        _wrapClient = clientFactory.GetClient(configured.Value.SourceId);
    }

    public async Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        if (dataKey.Length != EnvelopeFormat.DataKeySize)
        {
            throw new ArgumentException(EncryptionMessages.InvalidDataKeyLength, nameof(dataKey));
        }

        var result = await _wrapClient.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey, cancellationToken);

        if (!KeyVaultKeyIds.IsVersionOf(result.KeyId, _configuredKey) || !IsAllowedWrappedLength(result.EncryptedKey))
        {
            throw new InvalidOperationException(EncryptionMessages.KeyVaultReturnedUnexpectedKey);
        }

        return new WrappedDataKey(result.KeyId, Algorithm, result.EncryptedKey);
    }

    public async Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        if (!string.Equals(wrappedKey.Algorithm, Algorithm, StringComparison.Ordinal)
            || !KeyVaultKeyIds.IsVersionOf(wrappedKey.KeyId, _configuredKey))
        {
            throw new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);
        }

        if (!IsAllowedWrappedLength(wrappedKey.Value))
        {
            throw new SecretDecryptionException(DecryptionFailureReason.MalformedWrappedKey);
        }

        var client = _clientFactory.GetClient(new Uri(wrappedKey.KeyId));

        try
        {
            var result = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedKey.Value, cancellationToken);
            return result.Key;
        }
        catch (RequestFailedException exception) when (exception.Status == BadRequestStatus)
        {
            throw new SecretDecryptionException(DecryptionFailureReason.AuthenticationFailed);
        }
    }

    private static bool IsAllowedWrappedLength(byte[]? value) =>
        value?.Length is Rsa2048WrappedLength or Rsa3072WrappedLength or Rsa4096WrappedLength;
}
