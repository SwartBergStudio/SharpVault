// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Auth;

public interface ISessionActions
{
    string SignOutFormId { get; }

    string SignOutAction { get; }

    string AccessDeniedUrl { get; }

    string SignInUrl(string? returnUrl);

    bool IsSignInPage(string relativePath);
}
