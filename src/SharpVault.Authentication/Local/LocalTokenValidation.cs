// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;

namespace SharpVault.Authentication.Local;

public static class LocalTokenValidation
{
    public static TokenValidationParameters Create(LocalSigningKey signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = LocalAuthenticationDefaults.Issuer,
            ValidateAudience = true,
            ValidAudience = LocalAuthenticationDefaults.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey.Key,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = LocalAuthenticationDefaults.ClockSkew,
            ValidAlgorithms = [LocalAuthenticationDefaults.SigningAlgorithm],
            NameClaimType = EntraClaimTypes.Name,
            RoleClaimType = EntraClaimTypes.Roles,
        };
    }
}
