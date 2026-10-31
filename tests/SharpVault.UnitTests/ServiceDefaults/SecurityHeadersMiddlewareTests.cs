// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using SharpVault.ServiceDefaults.Security;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.ServiceDefaults;

[Trait("Category", "Security")]
public sealed class SecurityHeadersMiddlewareTests
{
    private static readonly string[] ConflictingFrameOptions = ["SAMEORIGIN", "ALLOWALL"];

    [Fact]
    public async Task InvokeAsync_AnyRequest_SetsEverySecurityHeaderWithExactValues()
    {
        var (context, feature) = CreateContext("/anything");

        await RunAsync(SecurityHeadersPolicy.ForApi(false), context, feature);

        var headers = context.Response.Headers;
        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, headers[SecurityHeaderNames.ContentSecurityPolicy]);
        Assert.Equal(SecurityHeaderValues.NoSniff, headers[SecurityHeaderNames.XContentTypeOptions]);
        Assert.Equal(SecurityHeaderValues.FrameDeny, headers[SecurityHeaderNames.XFrameOptions]);
        Assert.Equal(SecurityHeaderValues.NoReferrer, headers[SecurityHeaderNames.ReferrerPolicy]);
        Assert.Equal(SecurityHeaderValues.StrictTransportSecurity, headers[SecurityHeaderNames.StrictTransportSecurity]);
        Assert.Equal(SecurityHeaderValues.PermissionsPolicy, headers[SecurityHeaderNames.PermissionsPolicy]);
        Assert.Equal(SecurityHeaderValues.SameOrigin, headers[SecurityHeaderNames.CrossOriginOpenerPolicy]);
        Assert.Equal(SecurityHeaderValues.SameOrigin, headers[SecurityHeaderNames.CrossOriginResourcePolicy]);
        Assert.Equal(SecurityHeaderValues.NoStore, headers[SecurityHeaderNames.CacheControl]);
    }

    [Fact]
    public async Task InvokeAsync_WebPolicy_SendsWebContentSecurityPolicy()
    {
        var (context, feature) = CreateContext("/");

        await RunAsync(SecurityHeadersPolicy.ForWeb(), context, feature);

        Assert.Equal(SecurityHeaderValues.WebContentSecurityPolicy, context.Response.Headers[SecurityHeaderNames.ContentSecurityPolicy]);
    }

    [Fact]
    public async Task InvokeAsync_DownstreamSetsWeakerValues_OverwritesThemWithStrictValues()
    {
        var (context, feature) = CreateContext("/anything");

        await RunAsync(
            SecurityHeadersPolicy.ForApi(false),
            context,
            feature,
            ctx =>
            {
                ctx.Response.Headers[SecurityHeaderNames.XFrameOptions] = "SAMEORIGIN";
                ctx.Response.Headers[SecurityHeaderNames.XContentTypeOptions] = "none";
                ctx.Response.Headers[SecurityHeaderNames.ReferrerPolicy] = "unsafe-url";
                ctx.Response.Headers[SecurityHeaderNames.StrictTransportSecurity] = "max-age=0";
                ctx.Response.Headers[SecurityHeaderNames.PermissionsPolicy] = "camera=*";
                ctx.Response.Headers[SecurityHeaderNames.CrossOriginOpenerPolicy] = "unsafe-none";
                ctx.Response.Headers[SecurityHeaderNames.CrossOriginResourcePolicy] = "cross-origin";
                ctx.Response.Headers[SecurityHeaderNames.ContentSecurityPolicy] = "default-src *";
            });

        var headers = context.Response.Headers;
        Assert.Equal(SecurityHeaderValues.FrameDeny, headers[SecurityHeaderNames.XFrameOptions]);
        Assert.Equal(SecurityHeaderValues.NoSniff, headers[SecurityHeaderNames.XContentTypeOptions]);
        Assert.Equal(SecurityHeaderValues.NoReferrer, headers[SecurityHeaderNames.ReferrerPolicy]);
        Assert.Equal(SecurityHeaderValues.StrictTransportSecurity, headers[SecurityHeaderNames.StrictTransportSecurity]);
        Assert.Equal(SecurityHeaderValues.PermissionsPolicy, headers[SecurityHeaderNames.PermissionsPolicy]);
        Assert.Equal(SecurityHeaderValues.SameOrigin, headers[SecurityHeaderNames.CrossOriginOpenerPolicy]);
        Assert.Equal(SecurityHeaderValues.SameOrigin, headers[SecurityHeaderNames.CrossOriginResourcePolicy]);
        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, headers[SecurityHeaderNames.ContentSecurityPolicy]);
    }

    [Fact]
    public async Task InvokeAsync_DownstreamSetsMultipleValuesForSingleValuedHeader_ReplacesWithSingleValue()
    {
        var (context, feature) = CreateContext("/anything");

        await RunAsync(
            SecurityHeadersPolicy.ForApi(false),
            context,
            feature,
            ctx => ctx.Response.Headers.Append(SecurityHeaderNames.XFrameOptions, ConflictingFrameOptions));

        Assert.Equal([SecurityHeaderValues.FrameDeny], context.Response.Headers[SecurityHeaderNames.XFrameOptions].ToArray());
    }

    [Fact]
    public async Task InvokeAsync_DownstreamSetsCacheControl_KeepsExplicitValue()
    {
        var (context, feature) = CreateContext("/anything");

        await RunAsync(
            SecurityHeadersPolicy.ForApi(false),
            context,
            feature,
            ctx => ctx.Response.Headers[SecurityHeaderNames.CacheControl] = "no-store, no-cache");

        Assert.Equal("no-store, no-cache", context.Response.Headers[SecurityHeaderNames.CacheControl]);
    }

    [Fact]
    public async Task InvokeAsync_DownstreamSetsPublicCacheControl_KeepsExplicitValue()
    {
        var (context, feature) = CreateContext("/anything");

        await RunAsync(
            SecurityHeadersPolicy.ForApi(false),
            context,
            feature,
            ctx => ctx.Response.Headers[SecurityHeaderNames.CacheControl] = "public, max-age=60");

        Assert.Equal("public, max-age=60", context.Response.Headers[SecurityHeaderNames.CacheControl]);
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/scalar/")]
    [InlineData("/scalar/v1")]
    [InlineData("/scalar/scalar.js")]
    public async Task InvokeAsync_ApiReferenceEnabledOnScalarPath_OmitsContentSecurityPolicyButKeepsOtherHeaders(string path)
    {
        var (context, feature) = CreateContext(path);

        await RunAsync(SecurityHeadersPolicy.ForApi(true), context, feature);

        var headers = context.Response.Headers;
        Assert.False(headers.ContainsKey(SecurityHeaderNames.ContentSecurityPolicy));
        Assert.Equal(SecurityHeaderValues.FrameDeny, headers[SecurityHeaderNames.XFrameOptions]);
        Assert.Equal(SecurityHeaderValues.NoSniff, headers[SecurityHeaderNames.XContentTypeOptions]);
        Assert.Equal(SecurityHeaderValues.StrictTransportSecurity, headers[SecurityHeaderNames.StrictTransportSecurity]);
    }

    [Theory]
    [InlineData("/scalarx")]
    [InlineData("/scalars/v1")]
    [InlineData("/api/scalar")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("//scalar")]
    public async Task InvokeAsync_ApiReferenceEnabledOnOtherPath_StillSendsContentSecurityPolicy(string path)
    {
        var (context, feature) = CreateContext(path);

        await RunAsync(SecurityHeadersPolicy.ForApi(true), context, feature);

        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, context.Response.Headers[SecurityHeaderNames.ContentSecurityPolicy]);
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/scalar/v1")]
    public async Task InvokeAsync_ApiReferenceDisabledOnScalarPath_SendsContentSecurityPolicy(string path)
    {
        var (context, feature) = CreateContext(path);

        await RunAsync(SecurityHeadersPolicy.ForApi(false), context, feature);

        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, context.Response.Headers[SecurityHeaderNames.ContentSecurityPolicy]);
    }

    [Fact]
    public async Task InvokeAsync_WebPolicyOnScalarPath_StillSendsContentSecurityPolicy()
    {
        var (context, feature) = CreateContext("/scalar/v1");

        await RunAsync(SecurityHeadersPolicy.ForWeb(), context, feature);

        Assert.Equal(SecurityHeaderValues.WebContentSecurityPolicy, context.Response.Headers[SecurityHeaderNames.ContentSecurityPolicy]);
    }

    [Fact]
    public async Task InvokeAsync_DownstreamReturnsErrorStatus_StillSendsSecurityHeaders()
    {
        var (context, feature) = CreateContext("/anything");

        await RunAsync(
            SecurityHeadersPolicy.ForApi(false),
            context,
            feature,
            ctx => ctx.Response.StatusCode = StatusCodes.Status500InternalServerError);

        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, context.Response.Headers[SecurityHeaderNames.ContentSecurityPolicy]);
        Assert.Equal(SecurityHeaderValues.FrameDeny, context.Response.Headers[SecurityHeaderNames.XFrameOptions]);
    }

    [Fact]
    public async Task InvokeAsync_BeforeResponseStarts_DoesNotSetHeadersEagerly()
    {
        var (context, _) = CreateContext("/anything");
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, SecurityHeadersPolicy.ForApi(false));

        await middleware.InvokeAsync(context);

        Assert.Empty(context.Response.Headers);
    }

    [Fact]
    public async Task InvokeAsync_Always_CallsNextExactlyOnce()
    {
        var (context, feature) = CreateContext("/anything");
        var calls = 0;

        await RunAsync(SecurityHeadersPolicy.ForApi(false), context, feature, _ => calls++);

        Assert.Equal(1, calls);
    }

    private static (DefaultHttpContext Context, FakeResponseFeature Feature) CreateContext(string path)
    {
        var feature = new FakeResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(feature);
        context.Features.Set<IHttpResponseBodyFeature>(feature);
        context.Request.Path = path;
        return (context, feature);
    }

    private static async Task RunAsync(
        SecurityHeadersPolicy policy,
        DefaultHttpContext context,
        FakeResponseFeature feature,
        Action<HttpContext>? downstream = null)
    {
        var middleware = new SecurityHeadersMiddleware(
            ctx =>
            {
                downstream?.Invoke(ctx);
                return Task.CompletedTask;
            },
            policy);

        await middleware.InvokeAsync(context);
        await feature.FireOnStartingAsync();
    }
}
