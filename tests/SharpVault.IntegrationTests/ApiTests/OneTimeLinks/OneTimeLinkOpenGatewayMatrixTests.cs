// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.Extensions.Options;
using SharpVault.Authentication.WebGateway;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkOpenGatewayMatrixTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private const string WrongKey = "wrong-web-gateway-key-0123456789abcdefgh";
    private const string OtherAgent = "SharpVault-Other-Browser/2.0";

    public static TheoryData<string> RejectedProofs =>
    [
        "NoProof",
        "UserToken",
        "AdminToken",
        "LockedUserToken",
        "OtherTenantToken",
        "WrongKey",
        "OutsideTheWindow",
        "AlteredClientIp",
        "AlteredAgent",
        "AlteredPath",
    ];

    [Theory]
    [MemberData(nameof(RejectedProofs))]
    public async Task OneTimeLinkOpenGatewayMatrix_EveryProof_ReturnsExpectedStatus(string proof)
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        await WarmUpAsync(TestUsers.LockedUser);
        await LockUserAsync(TestUsers.LockedUser.ObjectId);

        using var response = await SendRejectedAsync(proof, linkId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task OneTimeLinkOpenGatewayMatrix_ValidProofLiveLink_Returns200ThenNotFound()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));

        using (var first = await OpenAsync(linkId))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task OneTimeLinkOpenGatewayMatrix_ValidProofUnknownLink_Returns404()
    {
        using var response = await OpenAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void OneTimeLinkOpenGatewayMatrix_EnabledWithoutKeyOnTheApi_StopsTheHostAtStartup()
    {
        using var host = Factory.WithWebHostBuilder(builder => builder.UseSetting("WebGateway:Key", string.Empty));

        var error = Assert.Throws<OptionsValidationException>(() => host.CreateClient());

        Assert.Contains("WebGateway:Key", error.Message, StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage> SendRejectedAsync(string proof, Guid linkId)
    {
        var path = OpenRoute(linkId);
        if (proof is "UserToken" or "AdminToken" or "LockedUserToken" or "OtherTenantToken")
        {
            var user = proof switch
            {
                "AdminToken" => TestUsers.Admin,
                "LockedUserToken" => TestUsers.LockedUser,
                "OtherTenantToken" => TestUsers.OtherTenantUser,
                _ => TestUsers.User,
            };
            return await PostOpenWithoutProofAsync(linkId, user);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        switch (proof)
        {
            case "NoProof":
                break;
            case "WrongKey":
                TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent, key: WrongKey);
                break;
            case "OutsideTheWindow":
                TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent, DateTimeOffset.UtcNow.AddSeconds(-120));
                break;
            case "AlteredClientIp":
                TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent);
                request.Headers.Remove(WebGatewayHeaders.ClientIp);
                request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, "198.51.100.99");
                break;
            case "AlteredAgent":
                TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent);
                request.Headers.Remove(WebGatewayHeaders.ClientAgent);
                request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientAgent, EncodeAgent(OtherAgent));
                break;
            case "AlteredPath":
                TestWebGateway.AddLinkOpenProof(request, OpenRoute(Guid.NewGuid()), TestAddresses.Browser, OpenAgent);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(proof), proof, null);
        }

        return await Factory.CreateUnauthenticatedClient().SendAsync(request, Token);
    }

}
