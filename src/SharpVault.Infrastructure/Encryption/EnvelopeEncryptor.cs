// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Encryption;

namespace SharpVault.Infrastructure.Encryption;

public sealed partial class EnvelopeEncryptor(IKeyWrapProvider keyWrapProvider, ILogger<EnvelopeEncryptor> logger) : IEnvelopeEncryptor
{
    public async Task<IDataKeySession> CreateDataKeyAsync(CancellationToken cancellationToken)
    {
        var dataKey = GC.AllocateUninitializedArray<byte>(EnvelopeFormat.DataKeySize, pinned: true);
        RandomNumberGenerator.Fill(dataKey);

        try
        {
            var wrapped = await keyWrapProvider.WrapAsync(dataKey, cancellationToken);
            return new DataKeySession(dataKey, wrapped, logger);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
    }

    public async Task<IDataKeySession> OpenDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        var dataKey = await UnwrapAsync(wrappedKey, cancellationToken);
        return new DataKeySession(dataKey, wrappedKey, logger);
    }

    public async Task<WrappedDataKey> RewrapDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        var dataKey = await UnwrapAsync(wrappedKey, cancellationToken);
        try
        {
            return await keyWrapProvider.WrapAsync(dataKey, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private async Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        byte[] unwrapped;
        try
        {
            unwrapped = await keyWrapProvider.UnwrapAsync(wrappedKey, cancellationToken);
        }
        catch (SecretDecryptionException exception)
        {
            LogDataKeyOpenFailed(logger, exception.Reason);
            throw;
        }

        if (unwrapped.Length != EnvelopeFormat.DataKeySize)
        {
            CryptographicOperations.ZeroMemory(unwrapped);
            LogDataKeyOpenFailed(logger, DecryptionFailureReason.MalformedWrappedKey);
            throw new SecretDecryptionException(DecryptionFailureReason.MalformedWrappedKey);
        }

        return unwrapped;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A data key could not be opened: {Reason}")]
    private static partial void LogDataKeyOpenFailed(ILogger logger, DecryptionFailureReason reason);
}
