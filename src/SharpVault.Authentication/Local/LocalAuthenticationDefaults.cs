// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;

namespace SharpVault.Authentication.Local;

public static class LocalAuthenticationDefaults
{
    public const string Issuer = "https://sharpvault.invalid/local";
    public const string Audience = "api://sharpvault-local";
    public const string TenantId = "5a1d0000-0000-4000-8000-0000000000a1";
    public const string ClientId = "5a1d0000-0000-4000-8000-0000000000c1";
    public const string Instance = "https://login.microsoftonline.com/";
    public const string AccessScope = "access_as_user";
    public const string KeyId = "sharpvault-local";
    public const string SigningAlgorithm = SecurityAlgorithms.HmacSha256;
    public const int KeyLength = 64;

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static EntraApplicationSettings EntraSettings { get; } =
        new(new Uri(Instance), Guid.Parse(TenantId), Guid.Parse(ClientId));
}
