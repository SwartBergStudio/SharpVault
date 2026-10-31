// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;

namespace SharpVault.ServiceDefaults.Identity;

public static class EntraTokenChecks
{
    private const string GuidFormat = "D";

    public static TokenCheckResult CheckAccessToken(ClaimsPrincipal? principal, EntraApplicationSettings settings, string requiredScope)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var identityResult = CheckIdentity(principal, settings);
        if (identityResult != TokenCheckResult.Valid)
        {
            return identityResult;
        }

        if (principal!.FindAll(EntraClaimTypes.IdentityType)
            .Any(claim => string.Equals(claim.Value, EntraClaimTypes.AppOnlyIdentityType, StringComparison.Ordinal)))
        {
            return TokenCheckResult.AppOnlyToken;
        }

        return HasScope(principal, requiredScope) ? TokenCheckResult.Valid : TokenCheckResult.MissingScope;
    }

    public static TokenCheckResult CheckIdToken(ClaimsPrincipal? principal, EntraApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return CheckIdentity(principal, settings);
    }

    private static TokenCheckResult CheckIdentity(ClaimsPrincipal? principal, EntraApplicationSettings settings)
    {
        if (principal is null)
        {
            return TokenCheckResult.Invalid;
        }

        if (!TrySingleGuid(principal, EntraClaimTypes.TenantId, out var tenantId) || tenantId != settings.TenantId)
        {
            return TokenCheckResult.WrongTenant;
        }

        if (!TrySingleGuid(principal, EntraClaimTypes.ObjectId, out var objectId) || objectId == Guid.Empty)
        {
            return TokenCheckResult.InvalidObjectId;
        }

        return TokenCheckResult.Valid;
    }

    private static bool TrySingleGuid(ClaimsPrincipal principal, string claimType, out Guid value)
    {
        value = Guid.Empty;
        var claims = principal.FindAll(claimType).Take(2).ToArray();
        return claims.Length == 1 && Guid.TryParseExact(claims[0].Value, GuidFormat, out value);
    }

    private static bool HasScope(ClaimsPrincipal principal, string requiredScope)
    {
        var claims = principal.FindAll(EntraClaimTypes.Scope).Take(2).ToArray();
        if (claims.Length != 1)
        {
            return false;
        }

        var scopes = claims[0].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return scopes.Contains(requiredScope, StringComparer.Ordinal);
    }
}
