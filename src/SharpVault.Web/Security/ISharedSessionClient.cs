// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;

namespace SharpVault.Web.Security;

public interface ISharedSessionClient
{
    Task<SharedSessionState> CheckAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
