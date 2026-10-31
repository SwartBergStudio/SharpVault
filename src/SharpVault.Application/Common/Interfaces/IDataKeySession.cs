// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Encryption;
using SharpVault.Domain.Encryption;

namespace SharpVault.Application.Common.Interfaces;

/// <summary>
/// Owns one unwrapped data key. Not thread-safe. Dispose zeroes the key. Encrypt and ReEncrypt use a new random nonce every call.
/// </summary>
public interface IDataKeySession : IDisposable
{
    WrappedDataKey WrappedKey { get; }

    byte[] Encrypt(EncryptionContext context, string plaintext);

    /// <summary>
    /// Throws SecretDecryptionException when the envelope is malformed, has an unsupported version, or fails authentication for the given context.
    /// </summary>
    string Decrypt(EncryptionContext context, byte[] envelope);

    /// <summary>
    /// Decrypts with the source context and encrypts with the target context under the same data key, without creating a string. Used by approved transfers (TRF-7).
    /// </summary>
    byte[] ReEncrypt(EncryptionContext source, EncryptionContext target, byte[] envelope);
}
