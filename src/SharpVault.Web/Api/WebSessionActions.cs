// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.Web.Api;

public sealed class WebSessionActions : ISessionActions
{
    public string SignOutFormId => "sign-out-form";

    public string SignOutAction => AccountRoutes.SignOut;

    public string AccessDeniedUrl => AccountRoutes.AccessDenied;

    public bool IsSignInPage(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var path = "/" + relativePath.Split('?', '#')[0].Trim('/');
        return string.Equals(path, AccountRoutes.SignIn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, AccountRoutes.LocalSignIn, StringComparison.OrdinalIgnoreCase);
    }

    public string SignInUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl)
            ? AccountRoutes.SignIn
            : $"{AccountRoutes.SignIn}?{AccountRoutes.ReturnUrlParameter}={Uri.EscapeDataString(returnUrl)}";
}
