// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using SharpVault.Authentication.Identity;

namespace SharpVault.Api.Security;

public static class SignedInIdentityReader
{
    private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public static bool TryRead(ClaimsPrincipal principal, EntraApplicationSettings settings, [NotNullWhen(true)] out SignedInIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(settings);

        identity = null;
        var objectIds = principal.FindAll(EntraClaimTypes.ObjectId).ToList();
        if (objectIds.Count != 1
            || !CanonicalGuid.TryParse(objectIds[0].Value, out var userId)
            || userId == Guid.Empty)
        {
            return false;
        }

        var email = principal.FindFirst(EntraClaimTypes.Email)?.Value
            ?? principal.FindFirst(EntraClaimTypes.PreferredUsername)?.Value;

        identity = new SignedInIdentity(
            userId,
            principal.FindFirst(EntraClaimTypes.Name)?.Value,
            email,
            AccountClassifier.Classify(principal, settings),
            principal.IsInRole(AppRoles.Admin),
            ReadIssuedAt(principal));
        return true;
    }

    private static DateTimeOffset? ReadIssuedAt(ClaimsPrincipal principal)
    {
        var claims = principal.FindAll(EntraClaimTypes.IssuedAt).ToList();
        if (claims.Count != 1
            || !long.TryParse(claims[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds < MinUnixSeconds
            || seconds > MaxUnixSeconds)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
