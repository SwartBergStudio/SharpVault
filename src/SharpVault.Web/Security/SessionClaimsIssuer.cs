// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Security.Claims;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.Web.Security;

public static class SessionClaimsIssuer
{
    private const string GuidFormat = "N";

    public static void Add(ClaimsIdentity identity, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(timeProvider);

        foreach (var existing in identity.FindAll(c => c.Type is SessionClaims.SessionId or SessionClaims.ExpiresAt).ToList())
        {
            identity.RemoveClaim(existing);
        }

        var expires = timeProvider.GetUtcNow().Add(SessionLifetime.Value).ToUnixTimeSeconds();
        identity.AddClaim(new Claim(SessionClaims.SessionId, Guid.NewGuid().ToString(GuidFormat)));
        identity.AddClaim(new Claim(SessionClaims.ExpiresAt, expires.ToString(CultureInfo.InvariantCulture)));
    }
}
