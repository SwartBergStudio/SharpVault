// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultResponsesTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private const string VaultPathPrefix = "/api/vault/";
    private const int VaultOperationCount = 20;

    public static TheoryData<string> EndpointIds() => [.. VaultEndpointCalls.AllIds];

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task EveryVaultResponse_HasNoStoreAndSecurityHeaders(string endpointId)
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(TestUsers.User));
        var responses = new List<HttpResponseMessage>
        {
            await VaultEndpointCalls.SendAsync(endpointId, ApiFor(TestUsers.OtherUser), scenario),
            await VaultEndpointCalls.SendAsync(endpointId, new VaultTestApi(Factory.CreateUnauthenticatedClient()), scenario),
            await VaultEndpointCalls.SendAsync(endpointId, ApiFor(TestUsers.AuthenticatedWithoutRole), scenario),
        };
        responses.Add(await VaultEndpointCalls.SendAsync(endpointId, ApiFor(TestUsers.User), scenario));

        Assert.Equal(4, responses.Count);
        foreach (var response in responses)
        {
            SecurityHeaderAssertions.AssertApi(response);
            Assert.True(response.Headers.CacheControl?.NoStore == true, $"{endpointId} answered {response.StatusCode} without no-store.");
        }
    }

    [Fact]
    public async Task OpenApiDocument_ListsAllTwentyVaultOperations()
    {
        using var client = Factory.CreateUnauthenticatedClient();
        var response = await client.GetAsync("/openapi/v1.json", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .Where(path => path.Name.StartsWith(VaultPathPrefix, StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject().Select(method => $"{method.Name.ToUpperInvariant()} {path.Name}"))
            .ToList();

        Assert.Equal(VaultOperationCount, operations.Count);
        Assert.Equal(VaultOperationCount, operations.Distinct(StringComparer.Ordinal).Count());
        foreach (var expected in new[]
        {
            "GET /api/vault/secrets", "POST /api/vault/secrets", "GET /api/vault/secrets/{secretId}", "PUT /api/vault/secrets/{secretId}",
            "DELETE /api/vault/secrets/{secretId}", "POST /api/vault/secrets/{secretId}/move", "POST /api/vault/secrets/{secretId}/reveal",
            "GET /api/vault/secrets/{secretId}/history", "POST /api/vault/secrets/{secretId}/history/{versionId}/reveal",
            "POST /api/vault/secrets/{secretId}/history/{versionId}/restore", "GET /api/vault/folders", "POST /api/vault/folders",
            "PUT /api/vault/folders/{folderId}", "POST /api/vault/folders/{folderId}/move", "DELETE /api/vault/folders/{folderId}",
            "GET /api/vault/recycle-bin", "POST /api/vault/recycle-bin/secrets/{secretId}/restore",
            "POST /api/vault/recycle-bin/folders/{folderId}/restore", "DELETE /api/vault/recycle-bin/secrets/{secretId}",
            "DELETE /api/vault/recycle-bin/folders/{folderId}",
        })
        {
            Assert.Contains(expected, operations);
        }
    }
}
