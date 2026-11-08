// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Primitives;

namespace SharpVault.IntegrationTests.Support;

public sealed class EmptyHeaderRestoringStartupFilter : IStartupFilter
{
    public const string ListHeader = "X-Test-Restore-Empty-Headers";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(async (context, pipeline) =>
            {
                foreach (var name in context.Request.Headers[ListHeader].SelectMany(value => value?.Split(',') ?? []))
                {
                    if (!context.Request.Headers.ContainsKey(name))
                    {
                        context.Request.Headers[name] = new StringValues(string.Empty);
                    }
                }

                context.Request.Headers.Remove(ListHeader);
                await pipeline();
            });
            next(app);
        };
    }
}
