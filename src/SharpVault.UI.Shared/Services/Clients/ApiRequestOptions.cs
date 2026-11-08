// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;

namespace SharpVault.UI.Shared.Services.Clients;

public static class ApiRequestOptions
{
    public static readonly HttpRequestOptionsKey<ClaimsPrincipal> Principal = new("SharpVault.Principal");
}
