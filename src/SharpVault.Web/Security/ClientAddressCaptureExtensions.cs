// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.Security;

public static class ClientAddressCaptureExtensions
{
    public static WebApplication UseClientAddressCapture(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                ClientAddressClaim.AddTo(context.User, context.Connection.RemoteIpAddress);
            }

            await next();
        });
        return app;
    }
}
