// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.Authentication.Identity;

namespace SharpVault.Authentication.WebGateway;

public static class WebGatewayScope
{
    public const string Format = "D";

    public static string For(Guid userId) => userId.ToString(Format);

    public static bool TryReadUserId(ClaimsPrincipal principal, out Guid userId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        userId = Guid.Empty;
        var objectIds = principal.FindAll(EntraClaimTypes.ObjectId).Take(2).ToArray();
        return objectIds is [{ } single] && CanonicalGuid.TryParse(single.Value, out userId);
    }
}
