// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.IdentityModel.Tokens;

namespace SharpVault.Api.Security;

internal static class ExactIssuerValidator
{
    private const string RejectionMessage = "The token issuer is not accepted.";

    public static IssuerValidator For(string expectedIssuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedIssuer);

        return (issuer, _, _) =>
            string.Equals(issuer, expectedIssuer, StringComparison.Ordinal)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException(RejectionMessage);
    }
}
