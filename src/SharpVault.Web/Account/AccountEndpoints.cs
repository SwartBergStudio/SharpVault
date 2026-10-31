// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using SharpVault.UI.Shared.Navigation;
using SharpVault.Web.Security;

namespace SharpVault.Web.Account;

public static class AccountEndpoints
{
    private const string InvalidRequestTitle = "The request is not valid.";

    public static WebApplication MapAccountEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(AccountRoutes.SignIn, SignIn)
            .AllowAnonymous()
            .WithName("Account_SignIn");

        app.MapPost(AccountRoutes.SignOut, SignOutAsync)
            .RequireAuthorization()
            .WithName("Account_SignOut");

        return app;
    }

    private static IResult SignIn(string? returnUrl)
    {
        var properties = new AuthenticationProperties { RedirectUri = ReturnUrls.Sanitize(returnUrl) };
        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidRequestTitle);
        }

        var properties = new AuthenticationProperties { RedirectUri = AccountRoutes.SignedOut };
        return Results.SignOut(
            properties,
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    }
}
