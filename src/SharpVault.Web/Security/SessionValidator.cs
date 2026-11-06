// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Security.Claims;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.Web.Security;

public sealed class SessionValidator(ISessionRevocationStore revocations, TimeProvider timeProvider) : ISessionValidator
{
    public async Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var sessionId = principal.FindFirst(SessionClaims.SessionId)?.Value;
        if (string.IsNullOrWhiteSpace(sessionId) || IsExpired(principal))
        {
            return false;
        }

        return !await revocations.IsRevokedAsync(sessionId, cancellationToken);
    }

    private bool IsExpired(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(SessionClaims.ExpiresAt)?.Value;
        return !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expires;
    }
}
