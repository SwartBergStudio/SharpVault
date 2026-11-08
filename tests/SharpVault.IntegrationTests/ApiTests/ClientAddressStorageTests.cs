// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net.Http.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.ApiTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(PerformanceTestGroup))]
[Trait("Category", "Performance")]
public sealed class ClientAddressStorageTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private const string MeRoute = "/api/me";

    [Fact]
    public async Task Me_WithAndWithoutProof_MakesTheSameStorageCalls()
    {
        using var client = ClientAddressProofs.ClientFrom(Factory.Server, ClientAddressProofs.ConnectionAddress, Caller);
        await SendAsync(client, HttpMethod.Get, MeRoute, null, proof: null);

        var without = await CountStorageCallsAsync(() => SendAsync(client, HttpMethod.Get, MeRoute, null, proof: null));
        var withProof = await CountStorageCallsAsync(() => SendAsync(client, HttpMethod.Get, MeRoute, null, ProofShape.Valid));

        Assert.Equal(without, withProof);
    }

    [Fact]
    public async Task Reveal_WithAndWithoutProof_MakesTheSameStorageCalls()
    {
        var secret = await NewSecretAsync();
        using var client = ClientAddressProofs.ClientFrom(Factory.Server, ClientAddressProofs.ConnectionAddress, Caller);
        var route = RevealRoute(secret.Id);
        await SendAsync(client, HttpMethod.Post, route, RevealBody(), proof: null);

        var without = await CountStorageCallsAsync(() => SendAsync(client, HttpMethod.Post, route, RevealBody(), proof: null));
        var withProof = await CountStorageCallsAsync(() => SendAsync(client, HttpMethod.Post, route, RevealBody(), ProofShape.Valid));

        Assert.Equal(without, withProof);
    }

    [Fact]
    public async Task Reveal_InvalidProof_MakesTheSameStorageCalls()
    {
        var secret = await NewSecretAsync();
        using var client = ClientAddressProofs.ClientFrom(Factory.Server, ClientAddressProofs.ConnectionAddress, Caller);
        var route = RevealRoute(secret.Id);
        await SendAsync(client, HttpMethod.Post, route, RevealBody(), proof: null);

        var without = await CountStorageCallsAsync(() => SendAsync(client, HttpMethod.Post, route, RevealBody(), proof: null));
        var invalid = await CountStorageCallsAsync(() => SendAsync(client, HttpMethod.Post, route, RevealBody(), ProofShape.WrongKey));

        Assert.Equal(without, invalid);
    }

    [Fact]
    public async Task AddressProof_AnyRequest_NoQueryWithoutPartitionKey()
    {
        var secret = await NewSecretAsync();
        using var client = ClientAddressProofs.ClientFrom(Factory.Server, ClientAddressProofs.ConnectionAddress, Caller);

        await SendAsync(client, HttpMethod.Get, MeRoute, null, ProofShape.Valid);
        await SendAsync(client, HttpMethod.Post, RevealRoute(secret.Id), RevealBody(), ProofShape.Valid);
        await SendAsync(client, HttpMethod.Post, RevealRoute(secret.Id), RevealBody(), ProofShape.WrongKey);

        AssertNoQueryWithoutPartitionKey();
    }

    private async Task<int> CountStorageCallsAsync(Func<Task> action)
    {
        StorageRequests.Clear();
        await action();
        return StorageRequests.Count;
    }

    private static string RevealRoute(Guid secretId) => ApiRoutes.VaultSecretReveal.Replace("{secretId:guid}", secretId.ToString("D"));

    private static RevealSecretRequestDto RevealBody() => new() { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };

    private static async Task SendAsync(HttpClient client, HttpMethod method, string route, object? body, ProofShape? proof)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (proof is { } shape)
        {
            ClientAddressProofs.Apply(request, shape, Caller.ObjectId);
        }

        using var response = await client.SendAsync(request, Token);
    }
}
