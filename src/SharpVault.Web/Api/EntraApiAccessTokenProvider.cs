// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.Web.Api;

public sealed class EntraApiAccessTokenProvider(
    ITokenAcquisition tokenAcquisition,
    IOptions<SharpVaultApiOptions> options) : IApiAccessTokenProvider
{
    public bool InteractiveSignInAvailable => true;

    public async Task<ApiAccessToken> GetAccessTokenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        try
        {
            var token = await tokenAcquisition.GetAccessTokenForUserAsync(
                options.Value.Scopes,
                authenticationScheme: null,
                tenantId: null,
                userFlow: null,
                user: principal,
                tokenAcquisitionOptions: new TokenAcquisitionOptions { CancellationToken = cancellationToken });
            return ApiAccessToken.Bearer(token);
        }
        catch (Exception exception) when (exception is MicrosoftIdentityWebChallengeUserException or MsalUiRequiredException)
        {
            return ApiAccessToken.SignInRequired;
        }
    }
}
