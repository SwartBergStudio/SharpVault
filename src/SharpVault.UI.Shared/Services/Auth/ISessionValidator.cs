// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;

namespace SharpVault.UI.Shared.Services.Auth;

public interface ISessionValidator
{
    Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
