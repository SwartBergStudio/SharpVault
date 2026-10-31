// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;

namespace SharpVault.Infrastructure.Encryption.Development;

public sealed class DevelopmentKeyWrapProvider : IKeyWrapProvider, IDisposable
{
    public const string Algorithm = "development-A256GCM";
    public const string KeyIdPrefix = "development:";
    public const int WrappedKeyLength = EnvelopeFormat.NonceSize + EnvelopeFormat.DataKeySize + EnvelopeFormat.TagSize;

    private const int KeyIdFingerprintLength = 8;
    private const int CiphertextEnd = EnvelopeFormat.NonceSize + EnvelopeFormat.DataKeySize;

    private static readonly byte[] KeyEncryptionKeyInfo = "SharpVault development key encryption key v1"u8.ToArray();
    private static readonly byte[] KeyIdInfo = "SharpVault development key id v1"u8.ToArray();

    private readonly byte[] _keyEncryptionKey;
    private readonly byte[] _associatedData;

    public DevelopmentKeyWrapProvider(IHostEnvironment environment, IOptions<EncryptionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(EncryptionMessages.DevelopmentProviderNotAllowed);
        }

        var material = options.Value.DevelopmentKeyMaterial?.Trim();
        if (material is null || material.Length < EncryptionSettingsGuard.MinimumDevelopmentKeyMaterialLength)
        {
            throw new InvalidOperationException(EncryptionMessages.DevelopmentKeyMaterialMissing);
        }

        var inputKeyMaterial = Encoding.UTF8.GetBytes(material);
        try
        {
            _keyEncryptionKey = GC.AllocateUninitializedArray<byte>(EnvelopeFormat.DataKeySize, pinned: true);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyMaterial, _keyEncryptionKey, [], KeyEncryptionKeyInfo);
            var fingerprint = HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyMaterial, KeyIdFingerprintLength, [], KeyIdInfo);
            KeyId = KeyIdPrefix + Convert.ToHexStringLower(fingerprint);
            _associatedData = Encoding.UTF8.GetBytes(KeyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputKeyMaterial);
        }
    }

    public string KeyId { get; }

    public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        if (dataKey.Length != EnvelopeFormat.DataKeySize)
        {
            throw new ArgumentException(EncryptionMessages.InvalidDataKeyLength, nameof(dataKey));
        }

        var wrapped = new byte[WrappedKeyLength];
        RandomNumberGenerator.Fill(wrapped.AsSpan(0, EnvelopeFormat.NonceSize));

        using var aesGcm = new AesGcm(_keyEncryptionKey, EnvelopeFormat.TagSize);
        aesGcm.Encrypt(
            wrapped.AsSpan(0, EnvelopeFormat.NonceSize),
            dataKey,
            wrapped.AsSpan(EnvelopeFormat.NonceSize, EnvelopeFormat.DataKeySize),
            wrapped.AsSpan(CiphertextEnd, EnvelopeFormat.TagSize),
            _associatedData);

        return Task.FromResult(new WrappedDataKey(KeyId, Algorithm, wrapped));
    }

    public Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        if (!string.Equals(wrappedKey.Algorithm, Algorithm, StringComparison.Ordinal)
            || !string.Equals(wrappedKey.KeyId, KeyId, StringComparison.Ordinal))
        {
            throw new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);
        }

        var value = wrappedKey.Value;
        if (value is null || value.Length != WrappedKeyLength)
        {
            throw new SecretDecryptionException(DecryptionFailureReason.MalformedWrappedKey);
        }

        var dataKey = GC.AllocateUninitializedArray<byte>(EnvelopeFormat.DataKeySize, pinned: true);
        try
        {
            using var aesGcm = new AesGcm(_keyEncryptionKey, EnvelopeFormat.TagSize);
            aesGcm.Decrypt(
                value.AsSpan(0, EnvelopeFormat.NonceSize),
                value.AsSpan(EnvelopeFormat.NonceSize, EnvelopeFormat.DataKeySize),
                value.AsSpan(CiphertextEnd, EnvelopeFormat.TagSize),
                dataKey,
                _associatedData);
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw new SecretDecryptionException(DecryptionFailureReason.AuthenticationFailed);
        }

        return Task.FromResult(dataKey);
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_keyEncryptionKey);
}
