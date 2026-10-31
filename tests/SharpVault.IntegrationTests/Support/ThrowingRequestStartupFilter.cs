// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace SharpVault.IntegrationTests.Support;

public sealed class ThrowingRequestStartupFilter : IStartupFilter
{
    public const string Path = "/test/throw";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            next(app);
            app.Map(Path, branch => branch.Run(_ => throw new InvalidOperationException(SensitiveMarkers.ExceptionMessage)));
        };
    }
}
