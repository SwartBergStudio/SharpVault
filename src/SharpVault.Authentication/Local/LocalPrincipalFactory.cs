// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.Authentication.Identity;

namespace SharpVault.Authentication.Local;

public static class LocalPrincipalFactory
{
    private const string GuidFormat = "D";

    public static ClaimsPrincipal Create(LocalAccount account, string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);

        var claims = new List<Claim>
        {
            new(EntraClaimTypes.ObjectId, account.Id.ToString(GuidFormat)),
            new(EntraClaimTypes.TenantId, LocalAuthenticationDefaults.TenantId),
            new(EntraClaimTypes.Name, account.DisplayName),
            new(EntraClaimTypes.PreferredUsername, account.Email),
            new(EntraClaimTypes.Email, account.Email),
            new(EntraClaimTypes.AccountType, EntraClaimTypes.MemberAccountType),
            new(EntraClaimTypes.Scope, LocalAuthenticationDefaults.AccessScope),
        };
        claims.AddRange(account.Roles.Select(role => new Claim(EntraClaimTypes.Roles, role)));

        var identity = new ClaimsIdentity(claims, authenticationType, EntraClaimTypes.Name, EntraClaimTypes.Roles);
        return new ClaimsPrincipal(identity);
    }
}
