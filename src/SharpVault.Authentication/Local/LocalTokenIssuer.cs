// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;

namespace SharpVault.Authentication.Local;

public sealed class LocalTokenIssuer(LocalSigningKey signingKey, TimeProvider timeProvider) : ILocalTokenIssuer
{
    private const string GuidFormat = "D";
    private static readonly JsonWebTokenHandler Handler = new();

    public string Issue(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var account = Find(principal);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = LocalAuthenticationDefaults.Issuer,
            Audience = LocalAuthenticationDefaults.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(LocalAuthenticationDefaults.TokenLifetime),
            Subject = BuildSubject(account),
            SigningCredentials = new SigningCredentials(signingKey.Key, LocalAuthenticationDefaults.SigningAlgorithm),
        };
        return Handler.CreateToken(descriptor);
    }

    private static LocalAccount Find(ClaimsPrincipal principal)
    {
        var identifiers = principal.FindAll(EntraClaimTypes.ObjectId).ToList();
        if (identifiers.Count != 1
            || !Guid.TryParseExact(identifiers[0].Value, GuidFormat, out var id)
            || !LocalAccounts.TryFind(id, out var account))
        {
            throw new InvalidOperationException(LocalAuthenticationMessages.AccountNotFound);
        }

        return account;
    }

    private static ClaimsIdentity BuildSubject(LocalAccount account)
    {
        var claims = new List<Claim>
        {
            new(EntraClaimTypes.ObjectId, account.Id.ToString(GuidFormat)),
            new(EntraClaimTypes.TenantId, LocalAuthenticationDefaults.TenantId),
            new(EntraClaimTypes.Name, account.DisplayName),
            new(EntraClaimTypes.PreferredUsername, account.Email),
            new(EntraClaimTypes.Email, account.Email),
            new(EntraClaimTypes.AccountType, EntraClaimTypes.MemberAccountType),
            new(EntraClaimTypes.Scope, LocalAuthenticationDefaults.AccessScope),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString(GuidFormat)),
        };
        claims.AddRange(account.Roles.Select(role => new Claim(EntraClaimTypes.Roles, role)));
        return new ClaimsIdentity(claims);
    }
}
