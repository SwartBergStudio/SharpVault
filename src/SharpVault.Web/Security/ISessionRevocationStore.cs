// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.Security;

public interface ISessionRevocationStore
{
    Task RevokeAsync(string sessionId, CancellationToken cancellationToken);

    Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken);
}
