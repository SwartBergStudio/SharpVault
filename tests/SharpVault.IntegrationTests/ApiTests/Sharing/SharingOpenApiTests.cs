// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Dtos;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingOpenApiTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly string[] Common = ["400", "401", "403", "429"];

    private static readonly Dictionary<string, string[]> DocumentedStatuses = new()
    {
        ["GET /api/shared/items"] = ["200", .. Common],
        ["GET /api/shared/owners/{ownerId}/secrets/{secretId}"] = ["200", "404", .. Common],
        ["PUT /api/shared/owners/{ownerId}/secrets/{secretId}"] = ["200", "404", "409", .. Common],
        ["POST /api/shared/owners/{ownerId}/secrets/{secretId}/reveal"] = ["200", "404", "500", .. Common],
        ["PUT /api/shared/owners/{ownerId}/secrets/{secretId}/filing"] = ["204", "404", .. Common],
        ["GET /api/shared/owners/{ownerId}/folders/{folderId}"] = ["200", "404", .. Common],
        ["PUT /api/shared/owners/{ownerId}/folders/{folderId}/filing"] = ["204", "404", .. Common],
        ["GET /api/sharing/owners/{ownerId}/secrets/{secretId}/grants"] = ["200", "404", .. Common],
        ["POST /api/sharing/owners/{ownerId}/secrets/{secretId}/grants"] = ["201", "404", "409", .. Common],
        ["PUT /api/sharing/owners/{ownerId}/secrets/{secretId}/grants/{recipientId}"] = ["200", "404", "409", .. Common],
        ["DELETE /api/sharing/owners/{ownerId}/secrets/{secretId}/grants/{recipientId}"] = ["204", "404", "409", .. Common],
        ["GET /api/sharing/owners/{ownerId}/folders/{folderId}/grants"] = ["200", "404", .. Common],
        ["POST /api/sharing/owners/{ownerId}/folders/{folderId}/grants"] = ["201", "404", "409", .. Common],
        ["PUT /api/sharing/owners/{ownerId}/folders/{folderId}/grants/{recipientId}"] = ["200", "404", "409", .. Common],
        ["DELETE /api/sharing/owners/{ownerId}/folders/{folderId}/grants/{recipientId}"] = ["204", "404", "409", .. Common],
        ["GET /api/directory/users"] = ["200", .. Common],
    };

    private static readonly Dictionary<string, string> RateLimitByPrefix = new()
    {
        ["POST /api/sharing/"] = RateLimitPolicies.Sharing,
        ["PUT /api/sharing/"] = RateLimitPolicies.Sharing,
        ["DELETE /api/sharing/"] = RateLimitPolicies.Sharing,
        ["GET /api/sharing/"] = RateLimitPolicies.Vault,
        ["GET /api/shared/"] = RateLimitPolicies.Vault,
        ["PUT /api/shared/"] = RateLimitPolicies.Vault,
        ["GET /api/directory/"] = RateLimitPolicies.DirectorySearch,
    };

    [Fact]
    public async Task OpenApi_SharingOperations_DeclareExactlyTheDocumentedStatuses()
    {
        using var document = await LoadOpenApiAsync();

        var declared = SharingOperations(document);

        Assert.Equal(DocumentedStatuses.Count, declared.Count);
        foreach (var (key, expected) in DocumentedStatuses)
        {
            Assert.True(declared.TryGetValue(key, out var actual), $"{key} is not in the OpenAPI document.");
            Assert.Equal(expected.Order(StringComparer.Ordinal), actual!.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void SharingEndpoints_Always_RequireTheUserPolicyAndTheDocumentedRateLimit()
    {
        var endpoints = SharingRouteEndpoints();

        Assert.Equal(DocumentedStatuses.Count, endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            var key = $"{endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {endpoint.RoutePattern.RawText}";
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToList();
            var limiter = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
            var expected = key.StartsWith("POST /api/shared/", StringComparison.Ordinal)
                ? RateLimitPolicies.Reveal
                : RateLimitByPrefix.First(pair => key.StartsWith(pair.Key, StringComparison.Ordinal)).Value;

            Assert.Contains(Policies.User, policies);
            Assert.Equal(expected, limiter);
        }
    }

    [Fact]
    public async Task SharingEndpoints_Unauthenticated_Return401()
    {
        using var client = CreateUnauthenticatedClient();
        var id = Guid.NewGuid().ToString("D");

        foreach (var endpoint in SharingRouteEndpoints())
        {
            var path = endpoint.RoutePattern.RawText!
                .Replace("{ownerId:guid}", id, StringComparison.Ordinal)
                .Replace("{secretId:guid}", id, StringComparison.Ordinal)
                .Replace("{folderId:guid}", id, StringComparison.Ordinal)
                .Replace("{recipientId:guid}", id, StringComparison.Ordinal);
            var method = new HttpMethod(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single());
            using var request = new HttpRequestMessage(method, path);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} returned {(int)response.StatusCode}.");
        }
    }

    [Fact]
    public void SharingRoutes_Always_AreTheConstantsInApiRoutes()
    {
        var routes = SharingRouteEndpoints().Select(endpoint => endpoint.RoutePattern.RawText).ToHashSet(StringComparer.Ordinal);
        string[] expected =
        [
            ApiRoutes.SharedItems,
            ApiRoutes.SharedSecret,
            ApiRoutes.SharedSecretReveal,
            ApiRoutes.SharedSecretFiling,
            ApiRoutes.SharedFolder,
            ApiRoutes.SharedFolderFiling,
            ApiRoutes.SecretGrants,
            ApiRoutes.SecretGrant,
            ApiRoutes.FolderGrants,
            ApiRoutes.FolderGrant,
            ApiRoutes.DirectoryUsers,
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), routes.Order(StringComparer.Ordinal));
    }

    private List<RouteEndpoint> SharingRouteEndpoints() =>
        [.. Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is { } text
                && (text.StartsWith("/api/shared/", StringComparison.Ordinal)
                    || text.StartsWith("/api/sharing/", StringComparison.Ordinal)
                    || text.StartsWith("/api/directory/", StringComparison.Ordinal)))];

    private async Task<JsonDocument> LoadOpenApiAsync()
    {
        using var client = CreateUnauthenticatedClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static Dictionary<string, string[]> SharingOperations(JsonDocument document)
    {
        var operations = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith("/api/shared", StringComparison.Ordinal)
                && !path.Name.StartsWith("/api/sharing", StringComparison.Ordinal)
                && !path.Name.StartsWith("/api/directory", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var method in path.Value.EnumerateObject())
            {
                operations[$"{method.Name.ToUpperInvariant()} {path.Name}"] = [.. method.Value.GetProperty("responses").EnumerateObject().Select(response => response.Name)];
            }
        }

        return operations;
    }
}
