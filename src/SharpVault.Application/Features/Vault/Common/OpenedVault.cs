// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;

namespace SharpVault.Application.Features.Vault.Common;

public sealed class OpenedVault(IDataKeySession session, int metadataKeyVersion, string keyRingETag) : IDisposable
{
    public IDataKeySession Session { get; } = session;

    public int MetadataKeyVersion { get; } = metadataKeyVersion;

    public string KeyRingETag { get; } = keyRingETag;

    public void Dispose() => Session.Dispose();
}
