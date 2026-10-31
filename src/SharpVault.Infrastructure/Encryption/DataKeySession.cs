// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Encryption;

namespace SharpVault.Infrastructure.Encryption;

public sealed partial class DataKeySession : IDataKeySession
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _dataKey;
    private readonly AesGcm _aesGcm;
    private readonly ILogger _logger;
    private bool _disposed;

    internal DataKeySession(byte[] dataKey, WrappedDataKey wrappedKey, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(wrappedKey);
        ArgumentNullException.ThrowIfNull(logger);

        _dataKey = dataKey;
        _aesGcm = new AesGcm(dataKey, EnvelopeFormat.TagSize);
        _logger = logger;
        WrappedKey = wrappedKey;
    }

    public WrappedDataKey WrappedKey { get; }

    public byte[] Encrypt(EncryptionContext context, string plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnvelopeAssociatedData.EnsureValid(context, nameof(context));
        ArgumentNullException.ThrowIfNull(plaintext);

        int length;
        try
        {
            length = StrictUtf8.GetByteCount(plaintext);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException(EncryptionMessages.PlaintextNotValidUnicode, nameof(plaintext));
        }

        if (length > EnvelopeFormat.MaxPlaintextBytes)
        {
            throw new ArgumentException(EncryptionMessages.PlaintextTooLarge, nameof(plaintext));
        }

        var envelope = new byte[EnvelopeFormat.Overhead + length];
        var body = envelope.AsSpan(EnvelopeFormat.HeaderSize, length);

        try
        {
            StrictUtf8.GetBytes(plaintext, body);
            Seal(context, envelope, body);
            return envelope;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(body);
            throw;
        }
    }

    public string Decrypt(EncryptionContext context, byte[] envelope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnvelopeAssociatedData.EnsureValid(context, nameof(context));
        ArgumentNullException.ThrowIfNull(envelope);

        var length = ReadPlaintextLength(context, envelope);
        var rented = ArrayPool<byte>.Shared.Rent(length);
        var plaintext = rented.AsSpan(0, length);

        try
        {
            Open(context, envelope, plaintext);
            return StrictUtf8.GetString(plaintext);
        }
        catch (DecoderFallbackException)
        {
            throw Fail(context, DecryptionFailureReason.MalformedEnvelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public byte[] ReEncrypt(EncryptionContext source, EncryptionContext target, byte[] envelope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnvelopeAssociatedData.EnsureValid(source, nameof(source));
        EnvelopeAssociatedData.EnsureValid(target, nameof(target));
        ArgumentNullException.ThrowIfNull(envelope);

        var length = ReadPlaintextLength(source, envelope);
        var reEncrypted = new byte[EnvelopeFormat.Overhead + length];
        var body = reEncrypted.AsSpan(EnvelopeFormat.HeaderSize, length);

        try
        {
            Open(source, envelope, body);
            Seal(target, reEncrypted, body);
            return reEncrypted;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(reEncrypted);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesGcm.Dispose();
        CryptographicOperations.ZeroMemory(_dataKey);
    }

    private void Seal(EncryptionContext context, byte[] envelope, Span<byte> body)
    {
        Span<byte> associatedData = stackalloc byte[EnvelopeAssociatedData.MaxLength];
        var written = EnvelopeAssociatedData.Write(EnvelopeFormat.Version1, context, associatedData);

        envelope[0] = EnvelopeFormat.Version1;
        var nonce = envelope.AsSpan(EnvelopeFormat.VersionSize, EnvelopeFormat.NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var tag = envelope.AsSpan(envelope.Length - EnvelopeFormat.TagSize);

        _aesGcm.Encrypt(nonce, body, body, tag, associatedData[..written]);
    }

    private void Open(EncryptionContext context, byte[] envelope, Span<byte> plaintext)
    {
        Span<byte> associatedData = stackalloc byte[EnvelopeAssociatedData.MaxLength];
        var written = EnvelopeAssociatedData.Write(EnvelopeFormat.Version1, context, associatedData);

        var nonce = envelope.AsSpan(EnvelopeFormat.VersionSize, EnvelopeFormat.NonceSize);
        var ciphertext = envelope.AsSpan(EnvelopeFormat.HeaderSize, plaintext.Length);
        var tag = envelope.AsSpan(envelope.Length - EnvelopeFormat.TagSize);

        try
        {
            _aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData[..written]);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw Fail(context, DecryptionFailureReason.AuthenticationFailed);
        }
    }

    private int ReadPlaintextLength(EncryptionContext context, byte[] envelope)
    {
        if (envelope.Length < EnvelopeFormat.Overhead || envelope.Length > EnvelopeFormat.MaxEnvelopeBytes)
        {
            throw Fail(context, DecryptionFailureReason.MalformedEnvelope);
        }

        if (envelope[0] != EnvelopeFormat.Version1)
        {
            throw Fail(context, DecryptionFailureReason.UnsupportedVersion);
        }

        return envelope.Length - EnvelopeFormat.Overhead;
    }

    private SecretDecryptionException Fail(EncryptionContext context, DecryptionFailureReason reason)
    {
        LogDecryptionFailed(_logger, context.ItemId, context.Purpose, reason);
        return new SecretDecryptionException(reason);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decryption failed for item {ItemId} with purpose {Purpose}: {Reason}")]
    private static partial void LogDecryptionFailed(ILogger logger, Guid itemId, string purpose, DecryptionFailureReason reason);
}
