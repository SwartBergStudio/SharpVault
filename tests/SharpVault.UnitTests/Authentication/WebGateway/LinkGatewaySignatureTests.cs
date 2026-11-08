// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Text;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.UnitTests.Authentication.WebGateway;

[Trait("Category", "Security")]
public sealed class LinkGatewaySignatureTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private const string FirstLinkPath = "/api/one-time-links/6f1c2b3a-0000-4000-8000-000000000001/open";
    private const string OtherLinkPath = "/api/one-time-links/6f1c2b3a-0000-4000-8000-000000000002/open";
    private const string ClientIp = "203.0.113.50";
    private const long UnixSeconds = 1_800_000_000;

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(UnixSeconds);
    private static readonly string FirstAgent = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("Browser A"));
    private static readonly string OtherAgent = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("Browser B"));

    [Fact]
    public void Signature_DifferentPath_DoesNotVerify()
    {
        var signature = Sign(WebGatewayScope.ForRoute("POST", FirstLinkPath), FirstAgent);

        Assert.True(Verify(WebGatewayScope.ForRoute("POST", FirstLinkPath), FirstAgent, signature));
        Assert.False(Verify(WebGatewayScope.ForRoute("POST", OtherLinkPath), FirstAgent, signature));
    }

    [Fact]
    public void Signature_DifferentAgent_DoesNotVerify()
    {
        var scope = WebGatewayScope.ForRoute("POST", FirstLinkPath);
        var signature = Sign(scope, FirstAgent);

        Assert.True(Verify(scope, FirstAgent, signature));
        Assert.False(Verify(scope, OtherAgent, signature));
    }

    [Fact]
    public void Sign_PathCase_IsNormalized()
    {
        var upper = FirstLinkPath.ToUpperInvariant();

        Assert.Equal(WebGatewayScope.ForRoute("POST", FirstLinkPath), WebGatewayScope.ForRoute("POST", upper));
        Assert.Equal(
            Sign(WebGatewayScope.ForRoute("POST", FirstLinkPath), FirstAgent),
            Sign(WebGatewayScope.ForRoute("POST", upper), FirstAgent));
    }

    private static string Sign(string scope, string agent) =>
        WebGatewaySignature.Sign(Key, WebGatewayPurposes.LinkOpen, scope, UnixSeconds, ClientIp, agent);

    private static bool Verify(string scope, string agent, string signature) =>
        WebGatewaySignature.Verify(Key, WebGatewayPurposes.LinkOpen, scope, UnixSeconds, ClientIp, agent, signature, Now);
}
