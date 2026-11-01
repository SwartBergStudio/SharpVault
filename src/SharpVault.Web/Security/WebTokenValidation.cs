// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UI.Shared.Navigation;

namespace SharpVault.Web.Security;

public static partial class WebTokenValidation
{
    private const string LoggerCategory = "SharpVault.Web.Security.WebTokenValidation";
    private const string RejectionMessage = "The sign-in is not accepted.";

    public static Task ValidateIdTokenAsync(TokenValidatedContext context, EntraApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        var result = EntraTokenChecks.CheckIdToken(context.Principal, settings);
        if (result == TokenCheckResult.Valid)
        {
            return Task.CompletedTask;
        }

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        LogIdTokenRejected(logger, result);
        context.Fail(RejectionMessage);
        return Task.CompletedTask;
    }

    public static Task RedirectRemoteFailureAsync(RemoteFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Redirect(AccountRoutes.SignInError);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in rejected by the ID token check: {Result}")]
    private static partial void LogIdTokenRejected(ILogger logger, TokenCheckResult result);
}
