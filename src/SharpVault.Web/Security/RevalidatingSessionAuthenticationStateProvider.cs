// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace SharpVault.Web.Security;

public sealed class RevalidatingSessionAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    SessionRevalidator sessionRevalidator) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        sessionRevalidator.IsStillValidAsync(authenticationState.User, cancellationToken);
}
