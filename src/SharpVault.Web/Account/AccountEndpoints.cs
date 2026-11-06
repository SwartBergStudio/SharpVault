// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using SharpVault.Authentication.Local;
using SharpVault.Authentication.Mode;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.Web.Security;

namespace SharpVault.Web.Account;

public static class AccountEndpoints
{
    public const string AccountIdField = "accountId";

    private const string InvalidRequestTitle = "The request is not valid.";
    private const string LocalAuthenticationType = "LocalAccount";
    private const string GuidFormat = "D";
    private const int PostOrder = -1;

    public static WebApplication MapAccountEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var local = app.Services.GetRequiredService<AuthenticationModeInfo>().Mode == AuthenticationMode.Local;

        Delegate signIn = local ? LocalSignIn : SignIn;
        Delegate signOut = local ? LocalSignOutAsync : SignOutAsync;

        app.MapGet(AccountRoutes.SignIn, signIn)
            .AllowAnonymous()
            .WithName("Account_SignIn");

        app.MapPost(AccountRoutes.SignOut, signOut)
            .RequireAuthorization()
            .WithName("Account_SignOut");

        if (local)
        {
            app.MapPost(AccountRoutes.LocalSignIn, LocalSignInAsync)
                .WithOrder(PostOrder)
                .AllowAnonymous()
                .WithName("Account_LocalSignIn");
        }

        return app;
    }

    private static IResult LocalSignIn(string? returnUrl) =>
        Results.LocalRedirect(
            AccountRoutes.LocalSignIn + "?" + AccountRoutes.ReturnUrlParameter + "="
            + Uri.EscapeDataString(ReturnUrls.Sanitize(returnUrl)));

    private static async Task<IResult> LocalSignInAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var form = await ReadValidatedFormAsync(context, antiforgery);
        if (form is null
            || !form.TryGetValue(AccountIdField, out var values)
            || values.Count != 1
            || !Guid.TryParseExact(values[0], GuidFormat, out var accountId)
            || !LocalAccounts.TryFind(accountId, out var account))
        {
            return InvalidRequest();
        }

        var returnUrl = form.TryGetValue(AccountRoutes.ReturnUrlParameter, out var returnValues) && returnValues.Count == 1
            ? returnValues[0]
            : null;

        var principal = LocalPrincipalFactory.Create(account, LocalAuthenticationType);
        SessionClaimsIssuer.Add((ClaimsIdentity)principal.Identity!, context.RequestServices.GetRequiredService<TimeProvider>());
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Results.LocalRedirect(ReturnUrls.Sanitize(returnUrl));
    }

    private static async Task<IResult> LocalSignOutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryValidAsync(context, antiforgery))
        {
            return InvalidRequest();
        }

        await RevokeSessionAsync(context);
        var properties = new AuthenticationProperties { RedirectUri = AccountRoutes.SignedOut };
        return Results.SignOut(properties, [CookieAuthenticationDefaults.AuthenticationScheme]);
    }

    private static IResult SignIn(string? returnUrl)
    {
        var properties = new AuthenticationProperties { RedirectUri = ReturnUrls.Sanitize(returnUrl) };
        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryValidAsync(context, antiforgery))
        {
            return InvalidRequest();
        }

        await RevokeSessionAsync(context);
        var properties = new AuthenticationProperties { RedirectUri = AccountRoutes.SignedOut };
        return Results.SignOut(
            properties,
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static async Task RevokeSessionAsync(HttpContext context)
    {
        var sessionId = context.User.FindFirst(SessionClaims.SessionId)?.Value;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await context.RequestServices.GetRequiredService<ISessionRevocationStore>()
                .RevokeAsync(sessionId, context.RequestAborted);
        }

        await RevokeSharedSessionAsync(context);
    }

    private static async Task RevokeSharedSessionAsync(HttpContext context)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        limit.CancelAfter(WebLimits.SharedRevokeTimeout);
        await context.RequestServices.GetRequiredService<ISharedSessionClient>()
            .RevokeAsync(context.User, limit.Token);
    }

    private static async Task<IFormCollection?> ReadValidatedFormAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!context.Request.HasFormContentType || !await IsAntiforgeryValidAsync(context, antiforgery))
        {
            return null;
        }

        return await context.Request.ReadFormAsync(context.RequestAborted);
    }

    private static async Task<bool> IsAntiforgeryValidAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            return await antiforgery.IsRequestValidAsync(context);
        }
        catch (Exception exception) when (exception is AntiforgeryValidationException or InvalidDataException or BadHttpRequestException)
        {
            return false;
        }
    }

    private static IResult InvalidRequest() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidRequestTitle);
}
