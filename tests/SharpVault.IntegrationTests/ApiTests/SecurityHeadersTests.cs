// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SecurityHeadersTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/does-not-exist")]
    [InlineData("/openapi/v1.json")]
    public async Task ApiResponse_AnyRoute_HasSecurityHeaders(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync(path, cancellationToken);

        SecurityHeaderAssertions.AssertApi(response);
    }

    [Theory]
    [InlineData("POST", "/health")]
    [InlineData("POST", "/does-not-exist")]
    [InlineData("OPTIONS", "/health")]
    [InlineData("DELETE", "/api/v1/secrets")]
    public async Task ApiResponse_NonGetMethods_HasSecurityHeaders(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.SendAsync(method, path, cancellationToken);

        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task ApiResponse_AuthenticatedCaller_HasSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task ApiResponse_WithoutExplicitCacheControl_SendsNoStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(SecurityHeaderValues.NoStore, response.HeaderValue(SecurityHeaderNames.CacheControl));
    }

    [Fact]
    public async Task ApiResponse_OpenApiDocument_SendsNoStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(SecurityHeaderValues.NoStore, response.HeaderValue(SecurityHeaderNames.CacheControl));
    }

    [Fact]
    public async Task ApiResponse_RequestSpoofsSecurityHeaders_ServerValuesWin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Add(SecurityHeaderNames.ContentSecurityPolicy, "default-src *");
        request.Headers.Add(SecurityHeaderNames.XFrameOptions, "ALLOWALL");

        var response = await Client.SendAsync(request, cancellationToken);

        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task ApiResponse_AnyRoute_DoesNotAdvertiseServerOrFramework()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Null(response.HeaderValue("Server"));
        Assert.Null(response.HeaderValue("X-Powered-By"));
        Assert.Null(response.HeaderValue("X-AspNet-Version"));
    }

    [Fact]
    public async Task ApiReference_Development_IsExemptFromContentSecurityPolicyButKeepsFrameDeny()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/scalar/v1", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy));
        Assert.Equal(SecurityHeaderValues.FrameDeny, response.HeaderValue(SecurityHeaderNames.XFrameOptions));
        SecurityHeaderAssertions.AssertBaseline(response);
    }

    [Fact]
    public async Task ApiReference_DevelopmentScriptAsset_IsExemptFromContentSecurityPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/scalar/scalar.js", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy));
    }

    [Theory]
    [InlineData("/scalar-evil")]
    [InlineData("/scalarx/v1")]
    [InlineData("/x/scalar/v1")]
    public async Task ApiReference_LookAlikePaths_StillGetContentSecurityPolicy(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync(path, cancellationToken);

        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy));
    }

    [Fact]
    public async Task ApiReference_PathCaseVariant_IsTreatedLikeScalarPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/SCALAR/v1", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy));
    }
}
