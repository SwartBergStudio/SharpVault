// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.UnitTests.ServiceDefaults;

public sealed class SecurityHeadersPolicyTests
{
    private const string IdentityProviderOrigin = "https://login.microsoftonline.com";

    private static readonly Uri IdentityProvider = new(IdentityProviderOrigin + "/");

    [Fact]
    public void ForApi_WithoutApiReferenceUi_UsesApiPolicyWithNoExemptPaths()
    {
        var policy = SecurityHeadersPolicy.ForApi(false);

        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, policy.ContentSecurityPolicy);
        Assert.Empty(policy.PathsWithoutContentSecurityPolicy);
    }

    [Fact]
    public void ForApi_WithApiReferenceUi_ExemptsOnlyScalarPath()
    {
        var policy = SecurityHeadersPolicy.ForApi(true);

        var exempt = Assert.Single(policy.PathsWithoutContentSecurityPolicy);
        Assert.Equal(new PathString("/scalar"), exempt);
        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, policy.ContentSecurityPolicy);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForWeb_Always_DisallowsInlineAndEvalScript()
    {
        var policy = SecurityHeadersPolicy.ForWeb(IdentityProvider);

        Assert.DoesNotContain("unsafe-inline", policy.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Empty(policy.PathsWithoutContentSecurityPolicy);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForApi_Always_DeniesEverySourceByDefault()
    {
        var policy = SecurityHeadersPolicy.ForApi(false);

        Assert.Contains("default-src 'none'", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(false)]
    [InlineData(true)]
    public void ForApi_Always_DisallowsUnsafeSourcesWildcardsAndFraming(bool allowApiReferenceUi)
    {
        var csp = SecurityHeadersPolicy.ForApi(allowApiReferenceUi).ContentSecurityPolicy;

        Assert.DoesNotContain("unsafe", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("*", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForWeb_Always_HasNoWildcardOrExternalSources()
    {
        var csp = SecurityHeadersPolicy.ForWeb(IdentityProvider).ContentSecurityPolicy;
        var withoutIdentityProvider = csp.Replace(" " + IdentityProviderOrigin, string.Empty, StringComparison.Ordinal);

        Assert.DoesNotContain("*", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("http:", withoutIdentityProvider, StringComparison.Ordinal);
        Assert.DoesNotContain("https:", withoutIdentityProvider, StringComparison.Ordinal);
        Assert.DoesNotContain("blob:", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("data:", csp.Replace("img-src 'self' data:", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(1, csp.Split("https://", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForWeb_IdentityProviderOrigin_AllowsOnlySelfAndThatOriginForFormAction()
    {
        var csp = SecurityHeadersPolicy.ForWeb(IdentityProvider).ContentSecurityPolicy;

        var directives = csp.Split("; ");
        var formAction = Assert.Single(directives, directive => directive.StartsWith("form-action ", StringComparison.Ordinal));
        Assert.Equal("form-action 'self' " + IdentityProviderOrigin, formAction);
        Assert.Equal(1, directives.Count(directive => directive.Contains("https://", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForWeb_InstanceWithPath_UsesOriginOnly()
    {
        var csp = SecurityHeadersPolicy.ForWeb(new Uri("https://login.microsoftonline.com/some/path?x=1")).ContentSecurityPolicy;

        Assert.Contains("form-action 'self' https://login.microsoftonline.com;", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("/some/path", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("x=1", csp, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("http://login.microsoftonline.com/")]
    [InlineData("ftp://login.microsoftonline.com/")]
    public void ForWeb_NonHttpsOrigin_Throws(string origin)
    {
        Assert.Throws<ArgumentException>(() => SecurityHeadersPolicy.ForWeb(new Uri(origin)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForWeb_RelativeOrigin_Throws()
    {
        Assert.Throws<ArgumentException>(() => SecurityHeadersPolicy.ForWeb(new Uri("/relative", UriKind.Relative)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForWeb_NullOrigin_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => SecurityHeadersPolicy.ForWeb(null!));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ForWeb_IdentityProvider_ProducesExactContentSecurityPolicy()
    {
        var csp = SecurityHeadersPolicy.ForWeb(IdentityProvider).ContentSecurityPolicy;

        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' https://login.microsoftonline.com; upgrade-insecure-requests", csp);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("default-src 'self'")]
    [InlineData("script-src 'self'")]
    [InlineData("style-src 'self'")]
    [InlineData("object-src 'none'")]
    [InlineData("frame-ancestors 'none'")]
    [InlineData("base-uri 'self'")]
    [InlineData("form-action 'self' https://login.microsoftonline.com")]
    [InlineData("upgrade-insecure-requests")]
    public void ForWeb_Always_ContainsDirective(string directive)
    {
        var csp = SecurityHeadersPolicy.ForWeb(IdentityProvider).ContentSecurityPolicy;

        Assert.Contains(directive, csp.Split("; "), StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SecurityHeaderValues_Constants_HaveExactExpectedText()
    {
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'", SecurityHeaderValues.ApiContentSecurityPolicy);
        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' {0}; upgrade-insecure-requests", SecurityHeaderValues.WebContentSecurityPolicyFormat);
        Assert.Equal("nosniff", SecurityHeaderValues.NoSniff);
        Assert.Equal("DENY", SecurityHeaderValues.FrameDeny);
        Assert.Equal("no-referrer", SecurityHeaderValues.NoReferrer);
        Assert.Equal("max-age=31536000; includeSubDomains", SecurityHeaderValues.StrictTransportSecurity);
        Assert.Equal("accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()", SecurityHeaderValues.PermissionsPolicy);
        Assert.Equal("same-origin", SecurityHeaderValues.SameOrigin);
        Assert.Equal("no-store", SecurityHeaderValues.NoStore);
        Assert.Equal("/scalar", SecurityHeaderValues.ApiReferencePath);
    }

    [Fact]
    public void SecurityHeaderNames_Constants_HaveExactHeaderNames()
    {
        Assert.Equal("Content-Security-Policy", SecurityHeaderNames.ContentSecurityPolicy);
        Assert.Equal("X-Content-Type-Options", SecurityHeaderNames.XContentTypeOptions);
        Assert.Equal("X-Frame-Options", SecurityHeaderNames.XFrameOptions);
        Assert.Equal("Referrer-Policy", SecurityHeaderNames.ReferrerPolicy);
        Assert.Equal("Strict-Transport-Security", SecurityHeaderNames.StrictTransportSecurity);
        Assert.Equal("Permissions-Policy", SecurityHeaderNames.PermissionsPolicy);
        Assert.Equal("Cross-Origin-Opener-Policy", SecurityHeaderNames.CrossOriginOpenerPolicy);
        Assert.Equal("Cross-Origin-Resource-Policy", SecurityHeaderNames.CrossOriginResourcePolicy);
        Assert.Equal("Cache-Control", SecurityHeaderNames.CacheControl);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StrictTransportSecurity_Value_LastsAtLeastOneYear()
    {
        var maxAge = SecurityHeaderValues.StrictTransportSecurity.Split(';')[0].Split('=')[1];

        Assert.True(long.Parse(maxAge, System.Globalization.CultureInfo.InvariantCulture) >= 31_536_000);
    }
}
