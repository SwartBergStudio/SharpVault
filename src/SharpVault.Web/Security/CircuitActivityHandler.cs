// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Idle;

namespace SharpVault.Web.Security;

public sealed partial class CircuitActivityHandler(
    CircuitActivityTracker tracker,
    AuthenticationStateProvider authentication,
    ISharedSessionClient shared,
    ISessionRevocationStore revocations,
    IOptions<IdleLockOptions> idleOptions,
    ILogger<CircuitActivityHandler> logger) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        context =>
        {
            tracker.Touch();
            return next(context);
        };

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        try
        {
            var principal = (await authentication.GetAuthenticationStateAsync()).User;
            if (principal.Identity?.IsAuthenticated != true || !SessionRevalidator.TryReadSession(principal, out var sessionId, out var expiresAt))
            {
                return;
            }

            tracker.Bind(sessionId, expiresAt);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(WebLimits.CircuitOpenCheckTimeout);
            await EndIfNotActiveAsync(principal, sessionId, limit.Token);
        }
        catch (Exception exception)
        {
            LogOpenCheckFailed(logger, exception.GetType().Name);
        }
    }

    private async Task EndIfNotActiveAsync(ClaimsPrincipal principal, string sessionId, CancellationToken cancellationToken)
    {
        if (tracker.IdleFor >= SessionRevalidator.ServerIdleLimit(idleOptions.Value.Timeout))
        {
            await revocations.RevokeAsync(sessionId, cancellationToken);
            await shared.RevokeAsync(principal, cancellationToken);
            return;
        }

        if (await shared.CheckAsync(principal, cancellationToken) == SharedSessionState.Ended)
        {
            await revocations.RevokeAsync(sessionId, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared session check on circuit open failed ({ExceptionType})")]
    private static partial void LogOpenCheckFailed(ILogger logger, string exceptionType);
}
