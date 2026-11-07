// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using SharpVault.Authentication.WebGateway;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.ApiTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class ClientAddressProofTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private const string JunkValue = ClientAddressProofs.JunkValue;

    public static TheoryData<ProofShape, string?> ProofRows() => new()
    {
        { ProofShape.None, ClientAddressProofs.ConnectionAddress },
        { ProofShape.AddressOnly, ClientAddressProofs.ConnectionAddress },
        { ProofShape.TimeAndAddressWithoutSignature, ClientAddressProofs.ConnectionAddress },
        { ProofShape.ForwardedHeaders, ClientAddressProofs.ConnectionAddress },
        { ProofShape.WrongKey, ClientAddressProofs.ConnectionAddress },
        { ProofShape.SignedForAnotherUser, ClientAddressProofs.ConnectionAddress },
        { ProofShape.LinkOpenPurpose, ClientAddressProofs.ConnectionAddress },
        { ProofShape.AddressAlteredAfterSigning, ClientAddressProofs.ConnectionAddress },
        { ProofShape.TwoMinutesOld, ClientAddressProofs.ConnectionAddress },
        { ProofShape.TwoMinutesInFuture, ClientAddressProofs.ConnectionAddress },
        { ProofShape.UnparsableTime, ClientAddressProofs.ConnectionAddress },
        { ProofShape.OverflowingTime, ClientAddressProofs.ConnectionAddress },
        { ProofShape.DuplicateSignature, ClientAddressProofs.ConnectionAddress },
        { ProofShape.MalformedSignature, ClientAddressProofs.ConnectionAddress },
        { ProofShape.Valid, ClientAddressProofs.ProofAddress },
        { ProofShape.ValidIpv6, "2001:db8::1" },
        { ProofShape.ValidEmptyAddress, null },
        { ProofShape.ValidUnparsableAddress, null },
    };

    [Theory]
    [MemberData(nameof(ProofRows))]
    public async Task ClientAddressProofMatrix_EveryProofShape_RecordsTheExpectedAddress(ProofShape shape, string? expected)
    {
        var secret = await NewSecretAsync();

        using var restored = Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, EmptyHeaderRestoringStartupFilter>()));
        var status = await RevealAsync(secret.Id, request => ClientAddressProofs.Apply(request, shape, Caller.ObjectId), restored.Server);

        Assert.Equal(HttpStatusCode.OK, status);
        var row = Assert.Single(await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed));
        Assert.Equal(expected, row.GetString("IpAddress"));
    }

    [Fact]
    public async Task Reveal_JunkProofHeaders_RowHoldsTheConnectionAddress()
    {
        var secret = await NewSecretAsync();

        var status = await RevealAsync(secret.Id, request =>
        {
            request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Time, JunkValue);
            request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, JunkValue);
            request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Signature, JunkValue);
        });

        Assert.Equal(HttpStatusCode.OK, status);
        var row = Assert.Single(await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed));
        Assert.Equal(ClientAddressProofs.ConnectionAddress, row.GetString("IpAddress"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AddressProof_AnyOutcome_LogsContainNoKeySignatureOrAddress()
    {
        var secret = await NewSecretAsync();
        var signatures = new List<string>();

        await RevealAsync(secret.Id, request =>
        {
            ClientAddressProofs.Apply(request, ProofShape.Valid, Caller.ObjectId);
            signatures.Add(request.Headers.GetValues(WebGatewayHeaders.Signature).Single());
        });
        await RevealAsync(secret.Id, request =>
        {
            ClientAddressProofs.Apply(request, ProofShape.WrongKey, Caller.ObjectId);
            signatures.Add(request.Headers.GetValues(WebGatewayHeaders.Signature).Single());
        });

        AssertLogsDoNotContain([TestWebGateway.Key, ClientAddressProofs.WrongKey, ClientAddressProofs.ProofAddress, .. signatures]);
    }

    [Fact]
    public async Task SignInRow_ValidProof_HoldsTheProofAddress()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        ClientAddressProofs.Apply(request, ProofShape.Valid, Caller.ObjectId);
        using var client = ClientAddressProofs.ClientFrom(Factory.Server, ClientAddressProofs.ConnectionAddress, Caller);

        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var signIn = Assert.Single(await ListAuditRowsAsync(), row => row.GetString("EventType") == nameof(AuditEventType.SignIn));
        Assert.Equal(ClientAddressProofs.ProofAddress, signIn.GetString("IpAddress"));
    }

    private async Task<HttpStatusCode> RevealAsync(Guid secretId, Action<HttpRequestMessage> applyProof, TestServer? server = null)
    {
        var route = ApiRoutes.VaultSecretReveal.Replace("{secretId:guid}", secretId.ToString("D"));
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }),
        };
        applyProof(request);
        using var client = ClientAddressProofs.ClientFrom(server ?? Factory.Server, ClientAddressProofs.ConnectionAddress, Caller);
        using var response = await client.SendAsync(request, Token);
        return response.StatusCode;
    }
}
