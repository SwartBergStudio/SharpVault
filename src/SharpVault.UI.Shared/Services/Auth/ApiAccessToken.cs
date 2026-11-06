// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Auth;

public sealed record ApiAccessToken(ApiAccessTokenKind Kind, string? Value)
{
    public static ApiAccessToken NoToken { get; } = new(ApiAccessTokenKind.None, null);

    public static ApiAccessToken SignInRequired { get; } = new(ApiAccessTokenKind.SignInRequired, null);

    public static ApiAccessToken Bearer(string value) => new(ApiAccessTokenKind.Bearer, value);
}
