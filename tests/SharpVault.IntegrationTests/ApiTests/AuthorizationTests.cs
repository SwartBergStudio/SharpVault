// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Api.Security;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.Authentication.Identity;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class AuthorizationTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly string[] AllowListedAnonymousPatterns =
    [
        "/health",
        "/alive",
        "/openapi/{documentName}.json",
        "/scalar/{documentName?}",
        "/scalar/scalar.js",
        "/scalar/scalar.aspnetcore.js",
        "/scalar/favicon.svg",
    ];

    public static TheoryData<string, CallerKind> HealthPathsAndCallers() => new()
    {
        { "/health", CallerKind.Anonymous },
        { "/alive", CallerKind.UnknownKeyToken },
        { "/alive", CallerKind.LockedAdmin },
    };

    public static TheoryData<string, CallerKind> DocumentationPathsAndCallers() => new()
    {
        { "/openapi/v1.json", CallerKind.Anonymous },
        { "/scalar/v1", CallerKind.AlgNoneToken },
        { "/openapi/v1.json", CallerKind.Admin },
    };

    public static TheoryData<string, string> UnknownRoutes() => new()
    {
        { "GET", "/does-not-exist" },
        { "PUT", "/api/v1/secrets" },
        { "DELETE", "/.env" },
    };

    [Theory]
    [MemberData(nameof(HealthPathsAndCallers))]
    public async Task HealthEndpoints_EveryCaller_Returns200(string path, CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(Factory, caller);

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(DocumentationPathsAndCallers))]
    public async Task Documentation_DevelopmentEveryCaller_Returns200Anonymously(string path, CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(Factory, caller);

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(UnknownRoutes))]
    public async Task UnknownRoute_AnonymousCaller_Returns401(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        StorageRequests.Clear();

        var response = await Client.SendAsync(method, path, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [MemberData(nameof(UnknownRoutes))]
    public async Task UnknownRoute_UserRoleCaller_Returns404(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        var response = await Client.SendAsync(method, path, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        StorageAssertions.AssertOnlyUserAndAuditTablesTouched(StorageRequests);
    }

    [Fact]
    public async Task AdminUsersRoute_UserRoleCaller_Returns403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        var response = await Client.GetAsync("/api/admin/users", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        StorageAssertions.AssertOnlyUserAndAuditTablesTouched(StorageRequests);
    }

    [Theory]
    [MemberData(nameof(UnknownRoutes))]
    public async Task UnknownRoute_NoRoleCaller_Returns403WithoutStorageCalls(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);
        StorageRequests.Clear();

        var response = await Client.SendAsync(method, path, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [InlineData("/api/v1/secrets")]
    public async Task UnknownRoute_AnonymousCallerWithTestHeadersOfNobody_Returns401(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        SpoofedTestHeaders.Add(request.Headers, TestUsers.Admin);

        var response = await Client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(CallerKind.AlgNoneToken)]
    [InlineData(CallerKind.UnknownKeyToken)]
    public async Task UnknownRoute_ForgedTokenWithoutTestIdentity_Returns401(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(Factory, caller);

        var response = await client.GetAsync("/api/v1/secrets", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Endpoints_EveryRoute_IsAllowListedAnonymousOrHasExplicitPolicy()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
            var isAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (isAnonymous)
            {
                Assert.Contains(pattern, AllowListedAnonymousPatterns);
                continue;
            }

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToList();
            Assert.True(
                policies.Any(policy => policy is Policies.User or Policies.Admin or Policies.LinkGateway),
                $"Endpoint '{pattern}' is not anonymous and has no explicit SharpVault authorization policy.");
        }
    }

    [Fact]
    public async Task FallbackPolicy_Configured_RequiresAuthenticatedUser()
    {
        var provider = Factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        AssertRequiresUserOrAdminRole(fallback);
    }

    [Fact]
    public async Task DefaultPolicy_Configured_RequiresAuthenticatedUser()
    {
        var provider = Factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await provider.GetDefaultPolicyAsync();

        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        AssertRequiresUserOrAdminRole(policy);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/openapi/v1.json")]
    public async Task CorsPreflight_UntrustedOrigin_ReturnsNoAllowOriginHeader(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await Client.SendAsync(request, cancellationToken);

        Assert.Null(response.HeaderValue("Access-Control-Allow-Origin"));
        Assert.Null(response.HeaderValue("Access-Control-Allow-Credentials"));
        Assert.Null(response.HeaderValue("Access-Control-Allow-Methods"));
    }

    [Theory]
    [InlineData("/health")]
    public async Task CrossOriginGet_UntrustedOrigin_ReturnsNoAllowOriginHeader(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", "https://evil.example");

        var response = await Client.SendAsync(request, cancellationToken);

        Assert.Null(response.HeaderValue("Access-Control-Allow-Origin"));
    }

    private static void AssertRequiresUserOrAdminRole(AuthorizationPolicy policy)
    {
        var access = Assert.Single(policy.Requirements.OfType<SharpVaultAccessRequirement>());
        Assert.Equal([AppRoles.User, AppRoles.Admin], access.AcceptedRoles);
    }

}
