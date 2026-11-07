// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Data.Tables;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.ApiTests.Sharing;
using SharpVault.IntegrationTests.ApiTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

public abstract class OneTimeLinkTestBase(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    protected const string OpenAgent = "SharpVault-Test-Browser/1.0";
    private const string VolatileTraceId = "traceId";
    private const string VolatileInstance = "instance";

    protected static string NewPayload(out string marker)
    {
        marker = "PLAINTEXT-" + Guid.NewGuid().ToString("N");
        var bytes = new byte[1 + marker.Length];
        bytes[0] = 0x80;
        Encoding.ASCII.GetBytes(marker, bytes.AsSpan(1));
        return Convert.ToBase64String(bytes);
    }

    protected static string SecretRoute(string template, Guid ownerId, Guid secretId) =>
        template
            .Replace("{ownerId:guid}", ownerId.ToString("D"), StringComparison.Ordinal)
            .Replace("{secretId:guid}", secretId.ToString("D"), StringComparison.Ordinal);

    protected static string LinkRoute(Guid linkId) =>
        ApiRoutes.OneTimeLink.Replace("{linkId:guid}", linkId.ToString("D"), StringComparison.Ordinal);

    protected static string OpenRoute(Guid linkId) =>
        ApiRoutes.OneTimeLinkOpen.Replace("{linkId:guid}", linkId.ToString("D"), StringComparison.Ordinal);

    protected async Task<HttpResponseMessage> PostCreateAsync(TestUser user, Guid ownerId, Guid secretId, string payload, int? lifetimeMinutes = null)
    {
        var client = ClientFor(user);
        return await client.PostAsJsonAsync(
            SecretRoute(ApiRoutes.OneTimeLinkSecret, ownerId, secretId),
            new CreateOneTimeLinkRequestDto { Payload = payload, LifetimeMinutes = lifetimeMinutes },
            Token);
    }

    protected async Task<Guid> CreateLinkAsync(Guid secretId, string payload)
    {
        using var response = await PostCreateAsync(Owner, OwnerId, secretId, payload);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(body);
        return body.Id;
    }

    protected async Task<HttpResponseMessage> PostSnapshotAsync(TestUser user, Guid ownerId, Guid secretId) =>
        await ClientFor(user).PostAsync(SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, ownerId, secretId), null, Token);

    protected async Task<HttpResponseMessage> RevokeAsync(TestUser user, Guid linkId) =>
        await ClientFor(user).DeleteAsync(LinkRoute(linkId), Token);

    protected async Task<HttpResponseMessage> OpenAsync(Guid linkId, string clientIp = TestAddresses.Browser, string agent = OpenAgent)
    {
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, clientIp, agent);
        return await Factory.CreateUnauthenticatedClient().SendAsync(request, Token);
    }

    protected async Task<HttpResponseMessage> PostOpenWithoutProofAsync(Guid linkId, TestUser? user = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OpenRoute(linkId));
        if (user is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        }

        return await Factory.CreateUnauthenticatedClient().SendAsync(request, Token);
    }

    protected async Task<OneTimeLinkNoticeListDto> OwnerNoticesAsync()
    {
        var response = await ClientFor(Owner).GetAsync(ApiRoutes.OneTimeLinkNotices, Token);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkNoticeListDto>(Token);
        Assert.NotNull(body);
        return body;
    }

    protected async Task<IReadOnlyList<TableEntity>> AuditRowsMentioningAsync(AuditEventType eventType, Guid id)
    {
        var needle = id.ToString("D");
        var rows = await VaultAuditRowsAsync(eventType);
        return [.. rows.Where(row => row.Any(pair => pair.Value?.ToString()?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true))];
    }

    protected async Task<IReadOnlyList<TableEntity>> AllAuditRowsMentioningAsync(Guid id)
    {
        var needle = id.ToString("D");
        var rows = await ListAuditRowsAsync();
        return [.. rows.Where(row => row.Any(pair => pair.Value?.ToString()?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true))];
    }

    protected static string NormalizedProblem(string body)
    {
        var node = JsonNode.Parse(body) as JsonObject
            ?? throw new InvalidOperationException("The response body is not a JSON object.");
        node.Remove(VolatileTraceId);
        node.Remove(VolatileInstance);
        return node.ToJsonString();
    }

    protected static string EncodeAgent(string agent) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(agent));
}
