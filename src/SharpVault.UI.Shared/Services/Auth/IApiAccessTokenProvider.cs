// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;

namespace SharpVault.UI.Shared.Services.Auth;

public interface IApiAccessTokenProvider
{
    bool InteractiveSignInAvailable { get; }

    Task<ApiAccessToken> GetAccessTokenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
