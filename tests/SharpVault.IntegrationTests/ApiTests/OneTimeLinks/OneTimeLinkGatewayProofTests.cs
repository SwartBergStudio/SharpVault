// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Authentication.WebGateway;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkGatewayProofTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private const string WrongKey = "wrong-web-gateway-key-0123456789abcdefgh";

    [Fact]
    public async Task GatewayProof_ClientAddressPurpose_OnTheOpenEndpoint_Returns401()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddProof(request, OwnerId, TestAddresses.Browser, purpose: WebGatewayPurposes.ClientAddress);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientAgent, EncodeAgent(OpenAgent));

        using var response = await Factory.CreateUnauthenticatedClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task LinkOpenProof_OnAUserEndpointWithAValidToken_RecordsTheConnectionAddress()
    {
        const string connectionAddress = "198.51.100.77";
        var secret = await NewSecretAsync();
        var path = SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, OwnerId, secret.Id);
        using var childFactory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LoopbackStartupFilter>()));
        using var client = ApiSecurityTestBase.CreateClientFor(childFactory);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(Owner));
        request.Headers.Add(LoopbackStartupFilter.RemoteAddressHeader, connectionAddress);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkSnapshotRevealed, secret.Id);
        var row = Assert.Single(rows);
        Assert.Equal(connectionAddress, row.GetString("IpAddress"));
        Assert.NotEqual(TestAddresses.Browser, row.GetString("IpAddress"));
    }

    [Fact]
    public async Task GatewayProof_TamperedClientIp_Returns401AndTheLinkStillOpens()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent);
        request.Headers.Remove(WebGatewayHeaders.ClientIp);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, "198.51.100.99");

        using var response = await Factory.CreateUnauthenticatedClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task GatewayProof_OutsideTheWindow_Returns401()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent, DateTimeOffset.UtcNow.AddSeconds(-120));

        using var response = await Factory.CreateUnauthenticatedClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GatewayProof_SignedWithAnotherKey_Returns401AndTheLinkStillOpens()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent, key: WrongKey);

        using var response = await Factory.CreateUnauthenticatedClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task GatewayProof_OnAUserEndpointWithNoToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        TestWebGateway.AddLinkOpenProof(request, MePath, TestAddresses.Browser, OpenAgent);

        using var response = await Factory.CreateUnauthenticatedClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(nameof(TestUsers.User))]
    [InlineData(nameof(TestUsers.Admin))]
    public async Task OpenEndpoint_SignedInUserToken_Returns401AndNeverConsumesTheLink(string userName)
    {
        var user = userName == nameof(TestUsers.Admin) ? TestUsers.Admin : TestUsers.User;
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));

        using var response = await PostOpenWithoutProofAsync(linkId, user);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }
}
