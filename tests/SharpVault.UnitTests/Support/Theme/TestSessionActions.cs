// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UnitTests.Support.Theme;

internal sealed class TestSessionActions : ISessionActions
{
    public string SignOutFormId => "sign-out-form";

    public string SignOutAction => "/account/sign-out";

    public string AccessDeniedUrl => "/account/access-denied";

    public bool IsSignInPage(string relativePath) =>
        relativePath.StartsWith("account/sign-in", StringComparison.Ordinal) || relativePath.StartsWith("account/local-sign-in", StringComparison.Ordinal);

    public string SignInUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl)
            ? "/account/sign-in"
            : $"/account/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";
}
