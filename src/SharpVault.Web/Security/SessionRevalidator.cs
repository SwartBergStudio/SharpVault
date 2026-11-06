// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Idle;

namespace SharpVault.Web.Security;

public sealed class SessionRevalidator(
    ISessionValidator sessionValidator,
    ISharedSessionClient shared,
    ISessionRevocationStore revocations,
    CircuitActivityTracker activity,
    IOptions<IdleLockOptions> idleOptions)
{
    public static readonly TimeSpan ServerIdleMargin = TimeSpan.FromMinutes(2);

    private const long MaxUnixSeconds = 253402300799;

    public static TimeSpan ServerIdleLimit(TimeSpan timeout) => timeout + ServerIdleMargin;

    public async Task<bool> IsStillValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!await sessionValidator.IsActiveAsync(principal, cancellationToken))
        {
            return false;
        }

        if (!TryReadSession(principal, out var sessionId, out var expiresAt))
        {
            return false;
        }

        activity.Bind(sessionId, expiresAt);
        if (activity.IdleFor >= ServerIdleLimit(idleOptions.Value.Timeout))
        {
            return await EndAsync(principal, share: true, cancellationToken);
        }

        return await shared.CheckAsync(principal, cancellationToken) is SharedSessionState.Ended or SharedSessionState.Forbidden
            ? await EndAsync(principal, share: false, cancellationToken)
            : true;
    }

    internal static bool TryReadSession(ClaimsPrincipal principal, out string sessionId, out DateTimeOffset expiresAt)
    {
        sessionId = principal.FindFirst(SessionClaims.SessionId)?.Value ?? string.Empty;
        expiresAt = default;
        var raw = principal.FindFirst(SessionClaims.ExpiresAt)?.Value;
        if (sessionId.Length == 0
            || !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 0 or > MaxUnixSeconds)
        {
            return false;
        }

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    private async Task<bool> EndAsync(ClaimsPrincipal principal, bool share, CancellationToken cancellationToken)
    {
        var sessionId = principal.FindFirst(SessionClaims.SessionId)?.Value;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await revocations.RevokeAsync(sessionId, cancellationToken);
        }

        if (share)
        {
            await shared.RevokeAsync(principal, cancellationToken);
        }

        return false;
    }
}
