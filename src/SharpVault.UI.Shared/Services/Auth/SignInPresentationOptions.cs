// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Auth;

public sealed class SignInPresentationOptions
{
    public const string DefaultHomeButtonText = "Sign in";

    public string HomeButtonText { get; set; } = DefaultHomeButtonText;
}
