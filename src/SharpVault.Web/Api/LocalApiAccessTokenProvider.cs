// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.Authentication.Local;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.Web.Api;

public sealed partial class LocalApiAccessTokenProvider(
    ILocalTokenIssuer issuer,
    ILogger<LocalApiAccessTokenProvider> logger) : IApiAccessTokenProvider
{
    public bool InteractiveSignInAvailable => false;

    public Task<ApiAccessToken> GetAccessTokenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        try
        {
            return Task.FromResult(ApiAccessToken.Bearer(issuer.Issue(principal)));
        }
        catch (InvalidOperationException)
        {
            LogAccountRejected(logger);
            return Task.FromResult(ApiAccessToken.SignInRequired);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The signed-in local account is not in the local account catalogue")]
    private static partial void LogAccountRejected(ILogger logger);
}
