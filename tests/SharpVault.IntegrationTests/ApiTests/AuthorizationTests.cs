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
using SharpVault.ServiceDefaults.Identity;

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

    private const string AdminUsersPath = "/api/admin/users";

    private static readonly string[] UnknownMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];

    private static readonly string[] UnknownPaths =
    [
        "/does-not-exist",
        "/api/v1/secrets",
        AdminUsersPath,
        "/health/extra",
        "/alive/extra",
        "/healthz",
        "/openapi",
        "/openapi/v1.json/extra",
        "/scalar-evil",
        "/.env",
        "/swagger/v1/swagger.json",
        "/test/throw",
    ];

    public static TheoryData<string, CallerKind> HealthPathsAndCallers() =>
        Cross(["/health", "/alive"], Enum.GetValues<CallerKind>());

    public static TheoryData<string, CallerKind> DocumentationPathsAndCallers() =>
        Cross(["/openapi/v1.json", "/scalar/v1"], Enum.GetValues<CallerKind>());

    public static TheoryData<string, string> UnknownRoutesAndMethods() => CrossMethods(UnknownMethods, UnknownPaths);

    public static TheoryData<string, string> UnknownRoutesWithoutExistingAdminRoute() =>
        CrossMethods(UnknownMethods, [.. UnknownPaths.Where(path => path != AdminUsersPath)]);

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
    [InlineData("/Health")]
    [InlineData("/HEALTH")]
    [InlineData("/Alive")]
    [InlineData("/ALIVE")]
    public async Task HealthEndpoints_DifferentCasing_Returns200Anonymously(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync(path, cancellationToken);

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
    [InlineData(CallerKind.Anonymous)]
    [InlineData(CallerKind.AuthenticatedWithoutRole)]
    public async Task OpenApiDocument_Development_Returns200Anonymously(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(Factory, caller);

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(CallerKind.Anonymous)]
    [InlineData(CallerKind.AuthenticatedWithoutRole)]
    public async Task ApiReference_Development_Returns200Anonymously(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(Factory, caller);

        var response = await client.GetAsync("/scalar/v1", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(UnknownRoutesAndMethods))]
    public async Task UnknownRoute_AnonymousCaller_Returns401(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        StorageRequests.Clear();

        var response = await Client.SendAsync(method, path, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [MemberData(nameof(UnknownRoutesWithoutExistingAdminRoute))]
    public async Task UnknownRoute_UserRoleCaller_Returns404(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        var response = await Client.SendAsync(method, path, cancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Unexpected status {(int)response.StatusCode} for {method} {path}");
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
    [MemberData(nameof(UnknownRoutesAndMethods))]
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
    [InlineData("GET", "/does-not-exist")]
    [InlineData("POST", "/does-not-exist")]
    [InlineData("GET", "/api/v1/secrets")]
    public async Task UnknownRoute_AuthenticatedCaller_Returns404Exactly(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.SendAsync(method, path, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/does-not-exist")]
    [InlineData("/api/v1/secrets")]
    [InlineData("/health/extra")]
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
                policies.Any(policy => policy is Policies.User or Policies.Admin),
                $"Endpoint '{pattern}' is not anonymous and has no explicit SharpVault authorization policy.");
        }
    }

    [Fact]
    public void Endpoints_Development_AnonymousRoutesAreOnlyHealthAndDocumentation()
    {
        var anonymous = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        Assert.Contains("/health", anonymous);
        Assert.Contains("/alive", anonymous);
        Assert.All(anonymous, pattern => Assert.Contains(pattern!, AllowListedAnonymousPatterns));
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
    [InlineData("/alive")]
    [InlineData("/does-not-exist")]
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
    [InlineData("/alive")]
    [InlineData("/does-not-exist")]
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

    private static TheoryData<string, CallerKind> Cross(string[] paths, CallerKind[] callers)
    {
        var data = new TheoryData<string, CallerKind>();
        foreach (var path in paths)
        {
            foreach (var caller in callers)
            {
                data.Add(path, caller);
            }
        }

        return data;
    }

    private static TheoryData<string, string> CrossMethods(string[] methods, string[] paths)
    {
        var data = new TheoryData<string, string>();
        foreach (var method in methods)
        {
            foreach (var path in paths)
            {
                data.Add(method, path);
            }
        }

        return data;
    }
}
