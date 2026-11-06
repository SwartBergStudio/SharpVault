// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Mode;
using SharpVault.UI.Shared.Navigation;

namespace SharpVault.Web.Security;

public static class LocalSignInGuardExtensions
{
    public static WebApplication UseLocalSignInGuard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var mode = app.Services.GetRequiredService<AuthenticationModeInfo>().Mode;
        var forwardedHeadersMode = LocalSignInGuard.IsForwardedHeadersModeEnabled(app.Configuration);
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(AccountRoutes.LocalSignIn, StringComparison.OrdinalIgnoreCase)
                && !LocalSignInGuard.Allows(
                    mode,
                    context.Connection.RemoteIpAddress,
                    context.Request.Host.Value,
                    forwardedHeadersMode || LocalSignInGuard.HasForwardedHeaders(context.Request.Headers)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });
        return app;
    }
}
