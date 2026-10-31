// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Encryption;

namespace SharpVault.Application.Common.Interfaces;

public interface IEnvelopeEncryptor
{
    Task<IDataKeySession> CreateDataKeyAsync(CancellationToken cancellationToken);

    Task<IDataKeySession> OpenDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken);

    Task<WrappedDataKey> RewrapDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken);
}
