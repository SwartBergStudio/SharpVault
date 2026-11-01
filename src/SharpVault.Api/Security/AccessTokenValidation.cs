// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharpVault.ServiceDefaults.Identity;

namespace SharpVault.Api.Security;

public static partial class AccessTokenValidation
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private const string RejectionMessage = "The access token is not accepted.";

    private const string LogCategory = "SharpVault.Api.Security.AccessTokenValidation";

    public static Task ValidateAsync(TokenValidatedContext context, EntraApplicationSettings settings)
    {
        var result = EntraTokenChecks.CheckAccessToken(context.Principal, settings, ApiScopes.AccessAsUser);
        if (result == TokenCheckResult.Valid)
        {
            return Task.CompletedTask;
        }

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
        LogTokenRejected(logger, result);
        context.Fail(RejectionMessage);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Access token rejected: {Reason}")]
    private static partial void LogTokenRejected(ILogger logger, TokenCheckResult reason);
}
