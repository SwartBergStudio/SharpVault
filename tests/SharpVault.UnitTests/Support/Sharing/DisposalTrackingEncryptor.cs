// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Encryption;

namespace SharpVault.UnitTests.Support.Sharing;

internal sealed class DisposalTrackingEncryptor(IEnvelopeEncryptor inner) : IEnvelopeEncryptor
{
    private int _opened;

    private int _disposed;

    public int Opened => Volatile.Read(ref _opened);

    public int Disposed => Volatile.Read(ref _disposed);

    public int Live => Opened - Disposed;

    public async Task<IDataKeySession> CreateDataKeyAsync(CancellationToken cancellationToken) =>
        new TrackedSession(await inner.CreateDataKeyAsync(cancellationToken), this);

    public async Task<IDataKeySession> OpenDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        var session = await inner.OpenDataKeyAsync(wrappedKey, cancellationToken);
        Interlocked.Increment(ref _opened);
        return new TrackedSession(session, this);
    }

    public Task<WrappedDataKey> RewrapDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken) =>
        inner.RewrapDataKeyAsync(wrappedKey, cancellationToken);

    private sealed class TrackedSession(IDataKeySession inner, DisposalTrackingEncryptor owner) : IDataKeySession
    {
        private int _disposedOnce;

        public WrappedDataKey WrappedKey => inner.WrappedKey;

        public byte[] Encrypt(EncryptionContext context, string plaintext) => inner.Encrypt(context, plaintext);

        public string Decrypt(EncryptionContext context, byte[] envelope) => inner.Decrypt(context, envelope);

        public byte[] ReEncrypt(EncryptionContext source, EncryptionContext target, byte[] envelope) => inner.ReEncrypt(source, target, envelope);

        public byte[] ReEncryptTo(EncryptionContext source, IDataKeySession targetSession, EncryptionContext targetContext, byte[] envelope) =>
            inner.ReEncryptTo(source, targetSession, targetContext, envelope);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedOnce, 1) == 0)
            {
                Interlocked.Increment(ref owner._disposed);
                inner.Dispose();
            }
        }
    }
}
