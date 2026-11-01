// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.ServiceDefaults.Identity;

namespace SharpVault.UnitTests.Support;

internal static class TestIdentities
{
    public static readonly Guid TenantId = Guid.Parse("3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b");

    public static readonly Guid ClientId = Guid.Parse("a1b2c3d4-e5f6-4708-8192-a3b4c5d6e7f8");

    public static readonly Guid OtherTenantId = Guid.Parse("9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a");

    public static EntraApplicationSettings Settings { get; } =
        new(new Uri("https://login.microsoftonline.com/"), TenantId, ClientId);

    public static ClaimsPrincipal Authenticated(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, "test", EntraClaimTypes.Name, EntraClaimTypes.Roles));

    public static ClaimsPrincipal Anonymous(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, null, EntraClaimTypes.Name, EntraClaimTypes.Roles));

    public static List<Claim> AccessTokenClaims(Guid? objectId = null) =>
    [
        new(EntraClaimTypes.TenantId, TenantId.ToString("D")),
        new(EntraClaimTypes.ObjectId, (objectId ?? Guid.NewGuid()).ToString("D")),
        new(EntraClaimTypes.Scope, "access_as_user"),
    ];

    public static List<Claim> MemberClaims(Guid objectId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(EntraClaimTypes.ObjectId, objectId.ToString("D")),
            new(EntraClaimTypes.TenantId, TenantId.ToString("D")),
            new(EntraClaimTypes.AccountType, EntraClaimTypes.MemberAccountType),
        };
        claims.AddRange(roles.Select(role => new Claim(EntraClaimTypes.Roles, role)));
        return claims;
    }

    public static List<Claim> Without(this List<Claim> claims, string claimType)
    {
        claims.RemoveAll(claim => claim.Type == claimType);
        return claims;
    }

    public static List<Claim> With(this List<Claim> claims, string claimType, string value)
    {
        claims.Add(new Claim(claimType, value));
        return claims;
    }

    public static List<Claim> Replacing(this List<Claim> claims, string claimType, string value) =>
        claims.Without(claimType).With(claimType, value);
}
