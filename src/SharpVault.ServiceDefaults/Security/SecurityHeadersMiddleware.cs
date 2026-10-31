// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;

namespace SharpVault.ServiceDefaults.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersPolicy policy)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(ApplySecurityHeaders, context);
        return next(context);
    }

    private Task ApplySecurityHeaders(object state)
    {
        var context = (HttpContext)state;
        var headers = context.Response.Headers;

        headers[SecurityHeaderNames.XContentTypeOptions] = SecurityHeaderValues.NoSniff;
        headers[SecurityHeaderNames.XFrameOptions] = SecurityHeaderValues.FrameDeny;
        headers[SecurityHeaderNames.ReferrerPolicy] = SecurityHeaderValues.NoReferrer;
        headers[SecurityHeaderNames.StrictTransportSecurity] = SecurityHeaderValues.StrictTransportSecurity;
        headers[SecurityHeaderNames.PermissionsPolicy] = SecurityHeaderValues.PermissionsPolicy;
        headers[SecurityHeaderNames.CrossOriginOpenerPolicy] = SecurityHeaderValues.SameOrigin;
        headers[SecurityHeaderNames.CrossOriginResourcePolicy] = SecurityHeaderValues.SameOrigin;

        if (!IsExemptFromContentSecurityPolicy(context.Request.Path))
        {
            headers[SecurityHeaderNames.ContentSecurityPolicy] = policy.ContentSecurityPolicy;
        }

        if (!headers.ContainsKey(SecurityHeaderNames.CacheControl))
        {
            headers[SecurityHeaderNames.CacheControl] = SecurityHeaderValues.NoStore;
        }

        return Task.CompletedTask;
    }

    private bool IsExemptFromContentSecurityPolicy(PathString requestPath)
    {
        foreach (var exempt in policy.PathsWithoutContentSecurityPolicy)
        {
            if (requestPath.StartsWithSegments(exempt))
            {
                return true;
            }
        }

        return false;
    }
}
