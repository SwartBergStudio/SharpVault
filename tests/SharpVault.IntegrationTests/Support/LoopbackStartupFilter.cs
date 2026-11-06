// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace SharpVault.IntegrationTests.Support;

public sealed class LoopbackStartupFilter : IStartupFilter
{
    public const string RemoteAddressHeader = "X-Test-Remote-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(async (context, pipeline) =>
            {
                var value = context.Request.Headers[RemoteAddressHeader].ToString();
                context.Connection.RemoteIpAddress = IPAddress.TryParse(value, out var address) ? address : IPAddress.Loopback;
                context.Request.Headers.Remove(RemoteAddressHeader);
                await pipeline();
            });
            next(app);
        };
    }
}
