// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authorization;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Access;
using SharpVault.ServiceDefaults.Identity;

namespace SharpVault.Api.Security;

public sealed partial class SharpVaultAccessHandler(
    IUserAccessGate gate,
    IGuestAccessPolicy guestPolicy,
    EntraApplicationSettings entra,
    ILogger<SharpVaultAccessHandler> logger) : AuthorizationHandler<SharpVaultAccessRequirement>
{
    private static readonly object DecisionKey = new();

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SharpVaultAccessRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (!SignedInIdentityReader.TryRead(context.User, entra, out var identity))
        {
            Deny(context, null, AccessDenialReason.InvalidIdentity);
            return;
        }

        var httpContext = context.Resource as HttpContext;
        var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;

        if (identity.AccountKind != AccountKind.Member && !await guestPolicy.AreGuestsAllowedAsync(cancellationToken))
        {
            Deny(context, identity.UserId, AccessDenialReason.Guest);
            return;
        }

        if (!requirement.AcceptedRoles.Any(context.User.IsInRole))
        {
            Deny(context, identity.UserId, AccessDenialReason.MissingRole);
            return;
        }

        var decision = await GetDecisionAsync(identity, httpContext, cancellationToken);
        if (decision == UserAccessDecision.Denied)
        {
            context.Fail(new AuthorizationFailureReason(this, nameof(AccessDenialReason.Locked)));
            return;
        }

        context.Succeed(requirement);
    }

    private async Task<UserAccessDecision> GetDecisionAsync(SignedInIdentity identity, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        if (httpContext?.Items.TryGetValue(DecisionKey, out var stored) == true && stored is UserAccessDecision memoized)
        {
            return memoized;
        }

        var user = new SignedInUser(identity.UserId, identity.DisplayName, identity.Email, httpContext?.Connection.RemoteIpAddress?.ToString());
        var decision = await gate.CheckAsync(user, cancellationToken);
        if (httpContext is not null)
        {
            httpContext.Items[DecisionKey] = decision;
        }

        return decision;
    }

    private void Deny(AuthorizationHandlerContext context, Guid? userId, AccessDenialReason reason)
    {
        context.Fail(new AuthorizationFailureReason(this, reason.ToString()));
        LogAccessDenied(logger, userId, reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Access denied for user {UserId}: {Reason}")]
    private static partial void LogAccessDenied(ILogger logger, Guid? userId, AccessDenialReason reason);
}
