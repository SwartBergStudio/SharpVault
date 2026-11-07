// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.TestHost;
using SharpVault.Authentication.WebGateway;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.Support;

public enum ProofShape
{
    None,
    AddressOnly,
    TimeAndAddressWithoutSignature,
    ForwardedHeaders,
    WrongKey,
    SignedForAnotherUser,
    LinkOpenPurpose,
    AddressAlteredAfterSigning,
    TwoMinutesOld,
    TwoMinutesInFuture,
    UnparsableTime,
    OverflowingTime,
    DuplicateSignature,
    MalformedSignature,
    Valid,
    ValidIpv6,
    ValidEmptyAddress,
    ValidUnparsableAddress,
}

public static class ClientAddressProofs
{
    public const string ConnectionAddress = "192.0.2.10";
    public const string ProofAddress = TestAddresses.Browser;
    public const string OtherProofAddress = "198.51.100.9";
    public const string Ipv6ProofAddress = "2001:DB8:0:0:0:0:0:1";
    public const string NotAnAddress = "not-an-ip";
    public const string WrongKey = "fedcba9876543210fedcba9876543210";
    public const string JunkValue = "junk";
    public const string ForwardedValue = "6.6.6.6";
    public const string ForwardedForHeader = "X-Forwarded-For";
    public const string RealIpHeader = "X-Real-IP";
    public const string ForwardedHeader = "Forwarded";
    public const string UnparsableTime = "abc";
    public const string OverflowingTime = "99999999999999999999";
    public const string BaseUri = "https://localhost";
    private const int TwoMinutes = 120;
    private const int MalformedSignatureLength = 43;

    public static HttpClient ClientFrom(TestServer server, string connectionAddress, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(user);

        var handler = server.CreateHandler(context => context.Connection.RemoteIpAddress = IPAddress.Parse(connectionAddress));
        var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUri) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    public static void Apply(HttpRequestMessage request, ProofShape shape, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        switch (shape)
        {
            case ProofShape.None:
                break;
            case ProofShape.AddressOnly:
                request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, ProofAddress);
                break;
            case ProofShape.TimeAndAddressWithoutSignature:
                request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Time, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
                request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, ProofAddress);
                break;
            case ProofShape.ForwardedHeaders:
                request.Headers.TryAddWithoutValidation(ForwardedForHeader, ForwardedValue);
                request.Headers.TryAddWithoutValidation(RealIpHeader, ForwardedValue);
                request.Headers.TryAddWithoutValidation(ForwardedHeader, $"for={ForwardedValue}");
                break;
            case ProofShape.WrongKey:
                TestWebGateway.AddProof(request, userId, ProofAddress, now, key: WrongKey);
                break;
            case ProofShape.SignedForAnotherUser:
                TestWebGateway.AddProof(request, TestUsers.OtherUser.ObjectId, ProofAddress, now);
                break;
            case ProofShape.LinkOpenPurpose:
                TestWebGateway.AddProof(request, userId, ProofAddress, now, purpose: WebGatewayPurposes.LinkOpen);
                break;
            case ProofShape.AddressAlteredAfterSigning:
                TestWebGateway.AddProof(request, userId, ProofAddress, now);
                Replace(request, WebGatewayHeaders.ClientIp, OtherProofAddress);
                break;
            case ProofShape.TwoMinutesOld:
                TestWebGateway.AddProof(request, userId, ProofAddress, now.AddSeconds(-TwoMinutes));
                break;
            case ProofShape.TwoMinutesInFuture:
                TestWebGateway.AddProof(request, userId, ProofAddress, now.AddSeconds(TwoMinutes));
                break;
            case ProofShape.UnparsableTime:
                TestWebGateway.AddProof(request, userId, ProofAddress, now);
                Replace(request, WebGatewayHeaders.Time, UnparsableTime);
                break;
            case ProofShape.OverflowingTime:
                TestWebGateway.AddProof(request, userId, ProofAddress, now);
                Replace(request, WebGatewayHeaders.Time, OverflowingTime);
                break;
            case ProofShape.DuplicateSignature:
                TestWebGateway.AddProof(request, userId, ProofAddress, now);
                request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Signature, request.Headers.GetValues(WebGatewayHeaders.Signature).Single());
                break;
            case ProofShape.MalformedSignature:
                TestWebGateway.AddProof(request, userId, ProofAddress, now);
                Replace(request, WebGatewayHeaders.Signature, new string('!', MalformedSignatureLength));
                break;
            case ProofShape.Valid:
                TestWebGateway.AddProof(request, userId, ProofAddress, now);
                break;
            case ProofShape.ValidIpv6:
                TestWebGateway.AddProof(request, userId, Ipv6ProofAddress, now);
                break;
            case ProofShape.ValidEmptyAddress:
                TestWebGateway.AddProof(request, userId, string.Empty, now);
                request.Headers.TryAddWithoutValidation(EmptyHeaderRestoringStartupFilter.ListHeader, WebGatewayHeaders.ClientIp);
                break;
            case ProofShape.ValidUnparsableAddress:
                TestWebGateway.AddProof(request, userId, NotAnAddress, now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    private static void Replace(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.TryAddWithoutValidation(name, value);
    }
}
