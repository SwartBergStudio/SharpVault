// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Builder;

namespace SharpVault.ServiceDefaults.Security;

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, SecurityHeadersPolicy policy) =>
        app.UseMiddleware<SecurityHeadersMiddleware>(policy);
}
