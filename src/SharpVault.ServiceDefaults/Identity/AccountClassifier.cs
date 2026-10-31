// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;

namespace SharpVault.ServiceDefaults.Identity;

public static class AccountClassifier
{
    public static AccountKind Classify(ClaimsPrincipal principal, EntraApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(settings);

        var accountTypes = principal.FindAll(EntraClaimTypes.AccountType).Take(2).ToArray();
        if (accountTypes.Length != 1)
        {
            return AccountKind.Unknown;
        }

        return accountTypes[0].Value switch
        {
            EntraClaimTypes.GuestAccountType => AccountKind.Guest,
            EntraClaimTypes.MemberAccountType => ClassifyMember(principal, settings),
            _ => AccountKind.Unknown,
        };
    }

    private static AccountKind ClassifyMember(ClaimsPrincipal principal, EntraApplicationSettings settings)
    {
        var foreignProvider = principal.FindAll(EntraClaimTypes.IdentityProvider)
            .Any(claim => !string.Equals(claim.Value, settings.V2Issuer, StringComparison.Ordinal)
                && !string.Equals(claim.Value, settings.V1Issuer, StringComparison.Ordinal));
        return foreignProvider ? AccountKind.Guest : AccountKind.Member;
    }
}
