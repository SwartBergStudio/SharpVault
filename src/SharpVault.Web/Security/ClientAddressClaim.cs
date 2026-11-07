// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Claims;

namespace SharpVault.Web.Security;

public static class ClientAddressClaim
{
    public const string Type = "sharpvault_client_address";

    public static void AddTo(ClaimsPrincipal principal, IPAddress? address)
    {
        ArgumentNullException.ThrowIfNull(principal);

        RemoveExisting(principal);
        if (address is null)
        {
            return;
        }

        var unmapped = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        principal.AddIdentity(new ClaimsIdentity([new Claim(Type, unmapped.ToString())]));
    }

    public static string? Read(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claims = principal.FindAll(Type).Take(2).ToArray();
        return claims is [{ } single] ? single.Value : null;
    }

    private static void RemoveExisting(ClaimsPrincipal principal)
    {
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.FindAll(Type).ToList())
            {
                identity.TryRemoveClaim(claim);
            }
        }
    }
}
