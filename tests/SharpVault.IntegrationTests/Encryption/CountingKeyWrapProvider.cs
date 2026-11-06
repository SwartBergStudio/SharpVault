// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;

namespace SharpVault.IntegrationTests.Encryption;

public sealed class CountingKeyWrapProvider(IKeyWrapProvider inner) : IKeyWrapProvider
{
    private int _wraps;
    private int _unwraps;

    public int Wraps => Volatile.Read(ref _wraps);

    public int Unwraps => Volatile.Read(ref _unwraps);

    public void Clear()
    {
        Interlocked.Exchange(ref _wraps, 0);
        Interlocked.Exchange(ref _unwraps, 0);
    }

    public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _wraps);
        return inner.WrapAsync(dataKey, cancellationToken);
    }

    public Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _unwraps);
        return inner.UnwrapAsync(wrappedKey, cancellationToken);
    }
}
