// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Support;

public static class TestCookiePrincipal
{
    public const string AuthenticationType = "TestCookie";

    private const string NameClaimType = "name";
    private const string RoleClaimType = "roles";

    public static ClaimsPrincipal Create(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var claims = new List<Claim>
        {
            new("oid", user.ObjectId.ToString("D")),
            new("tid", user.TenantId.ToString("D")),
            new(NameClaimType, user.DisplayName),
            new("preferred_username", user.Email ?? user.ObjectId.ToString("D")),
        };
        claims.AddRange(user.Roles.Select(role => new Claim(RoleClaimType, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, NameClaimType, RoleClaimType));
    }
}
