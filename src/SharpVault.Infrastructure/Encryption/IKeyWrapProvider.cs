// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Encryption;

namespace SharpVault.Infrastructure.Encryption;

/// <summary>
/// Wraps and unwraps data keys with a key-encryption key.
/// </summary>
public interface IKeyWrapProvider
{
    /// <summary>
    /// WrapAsync must not copy or keep <paramref name="dataKey"/>; the caller zeroes it.
    /// </summary>
    Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken);

    /// <summary>
    /// UnwrapAsync returns a new array that the caller owns and zeroes.
    /// Both methods throw SecretDecryptionException(KeyMismatch) for a wrapped key that belongs to another provider, key or algorithm.
    /// </summary>
    Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken);
}
