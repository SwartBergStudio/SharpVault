// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Support;

public sealed class TestCookieSignInStartupFilter : IStartupFilter
{
    public const string Path = "/test/sign-in";

    private const string IssuingSchemeKey = ".AuthScheme";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Map(Path, branch => branch.Run(SignInAsync));
            next(app);
        };
    }

    private static async Task SignInAsync(HttpContext context)
    {
        var lastSegment = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        var user = Guid.TryParse(lastSegment, out var oid)
            ? TestUsers.All.FirstOrDefault(candidate => candidate.ObjectId == oid)
            : null;
        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var properties = new AuthenticationProperties();
        properties.Items[IssuingSchemeKey] = OpenIdConnectDefaults.AuthenticationScheme;
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, TestCookiePrincipal.Create(user), properties);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
