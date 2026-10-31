// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using SharpVault.ServiceDefaults.Identity;

namespace SharpVault.Api.Security;

public static class SignedInIdentityReader
{
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
            principal.IsInRole(AppRoles.Admin));
        return true;
    }
}
