// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.IntegrationTests.Authentication;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.Support;

public static class SecurityHeaderAssertions
{
    public static void AssertBaseline(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        Assert.Equal(SecurityHeaderValues.NoSniff, response.HeaderValue(SecurityHeaderNames.XContentTypeOptions));
        Assert.Equal(SecurityHeaderValues.FrameDeny, response.HeaderValue(SecurityHeaderNames.XFrameOptions));
        Assert.Equal(SecurityHeaderValues.NoReferrer, response.HeaderValue(SecurityHeaderNames.ReferrerPolicy));
        Assert.Equal(SecurityHeaderValues.StrictTransportSecurity, response.HeaderValue(SecurityHeaderNames.StrictTransportSecurity));
        Assert.Equal(SecurityHeaderValues.PermissionsPolicy, response.HeaderValue(SecurityHeaderNames.PermissionsPolicy));
        Assert.Equal(SecurityHeaderValues.SameOrigin, response.HeaderValue(SecurityHeaderNames.CrossOriginOpenerPolicy));
        Assert.Equal(SecurityHeaderValues.SameOrigin, response.HeaderValue(SecurityHeaderNames.CrossOriginResourcePolicy));
    }

    public static void AssertApi(HttpResponseMessage response)
    {
        AssertBaseline(response);
        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy));
    }

    public static void AssertWeb(HttpResponseMessage response)
    {
        AssertBaseline(response);
        var csp = response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy);
        Assert.Equal(SecurityHeadersPolicy.ForWeb(TestIdentityProvider.InstanceUri).ContentSecurityPolicy, csp);
        Assert.Contains("form-action 'self' https://login.microsoftonline.com;", csp, StringComparison.Ordinal);
    }
}
