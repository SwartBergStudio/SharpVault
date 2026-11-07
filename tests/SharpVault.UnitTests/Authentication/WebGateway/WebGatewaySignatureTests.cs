// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.UnitTests.Authentication.WebGateway;

[Trait("Category", "Security")]
public sealed class WebGatewaySignatureTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private const string OtherKey = "fedcba9876543210fedcba9876543210";
    private const string Scope = "6f1c2b3a-0000-4000-8000-000000000001";
    private const string OtherScope = "6f1c2b3a-0000-4000-8000-000000000002";
    private const string ClientIp = "203.0.113.50";
    private const string OtherClientIp = "203.0.113.51";
    private const string Agent = "";
    private const string OtherAgent = "QnJvd3NlciBB";
    private const long UnixSeconds = 1_800_000_000;
    private const int WindowSeconds = 60;
    private const int SignatureLength = WebGatewaySignature.SignatureLength;

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(UnixSeconds);

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var signature = SignFor();

        Assert.True(VerifyFor(signature));
    }

    [Fact]
    public void Sign_Always_ReturnsA43CharacterBase64UrlSignature()
    {
        var signature = SignFor();

        Assert.Equal(WebGatewaySignature.SignatureLength, signature.Length);
        Assert.DoesNotContain('+', signature);
        Assert.DoesNotContain('/', signature);
        Assert.DoesNotContain('=', signature);
    }

    [Fact]
    public void Sign_KnownVector_MatchesAnHmacComputedIndependently()
    {
        const string message = "v1\nclient-address\n6f1c2b3a-0000-4000-8000-000000000001\n1800000000\n203.0.113.50\n";
        var expected = Base64Url.EncodeToString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), Encoding.UTF8.GetBytes(message)));

        Assert.Equal(expected, SignFor());
    }

    [Fact]
    public void Signature_DifferentPurpose_DoesNotVerify()
    {
        var signature = SignFor(purpose: WebGatewayPurposes.LinkOpen);

        Assert.True(VerifyFor(signature, purpose: WebGatewayPurposes.LinkOpen));
        Assert.False(VerifyFor(signature));
    }

    [Fact]
    public void Signature_DifferentScope_DoesNotVerify()
    {
        var signature = SignFor();

        Assert.False(VerifyFor(signature, scope: OtherScope));
    }

    [Fact]
    public void Signature_DifferentClientIp_DoesNotVerify()
    {
        var signature = SignFor();

        Assert.False(VerifyFor(signature, clientIp: OtherClientIp));
    }

    [Fact]
    public void Signature_DifferentAgent_DoesNotVerify()
    {
        var signature = SignFor(agent: OtherAgent);

        Assert.True(VerifyFor(signature, agent: OtherAgent));
        Assert.False(VerifyFor(signature));
    }

    [Fact]
    public void Signature_DifferentTime_DoesNotVerify()
    {
        var signature = SignFor();

        Assert.False(VerifyFor(signature, unixSeconds: UnixSeconds + 1));
    }

    [Fact]
    public void Signature_DifferentKey_DoesNotVerify()
    {
        var signature = SignFor(key: OtherKey);

        Assert.False(VerifyFor(signature));
    }

    [Fact]
    public void Verify_EmptyKey_ReturnsFalse()
    {
        var signature = SignFor();

        Assert.False(VerifyFor(signature, key: string.Empty));
    }

    [Fact]
    public void Verify_AtTheWindowBoundary_ReturnsTrue()
    {
        var signature = SignFor();

        Assert.True(VerifyFor(signature, now: Now.AddSeconds(WindowSeconds)));
        Assert.True(VerifyFor(signature, now: Now.AddSeconds(-WindowSeconds)));
    }

    [Fact]
    public void Verify_OneSecondOutsideTheWindow_ReturnsFalse()
    {
        var signature = SignFor();

        Assert.False(VerifyFor(signature, now: Now.AddSeconds(WindowSeconds + 1)));
        Assert.False(VerifyFor(signature, now: Now.AddSeconds(-(WindowSeconds + 1))));
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(253_402_300_800)]
    [InlineData(-62_135_596_801)]
    public void Verify_TimeThatOverflowsDateTimeOffset_ReturnsFalse(long unixSeconds)
    {
        var signature = SignFor(unixSeconds: unixSeconds);

        Assert.False(VerifyFor(signature, unixSeconds: unixSeconds));
    }

    [Fact]
    public void Verify_MalformedBase64_ReturnsFalse()
    {
        var signatures = new[]
        {
            new string('!', SignatureLength),
            new string('+', SignatureLength),
            new string('=', SignatureLength),
        };

        Assert.All(signatures, signature => Assert.False(VerifyFor(signature)));
    }

    [Fact]
    public void Verify_WrongLengthSignature_ReturnsFalse()
    {
        var signatures = new[]
        {
            string.Empty,
            "A",
            new string('A', SignatureLength - 1),
            new string('A', SignatureLength + 1),
        };

        Assert.All(signatures, signature => Assert.False(VerifyFor(signature)));
    }

    [Fact]
    public void Verify_ValidSignatureTruncatedByOneCharacter_ReturnsFalse()
    {
        var signature = SignFor();

        Assert.False(VerifyFor(signature[..^1]));
    }

    [Fact]
    public void Verify_NullSignature_ReturnsFalse()
    {
        Assert.False(WebGatewaySignature.Verify(Key, WebGatewayPurposes.ClientAddress, Scope, UnixSeconds, ClientIp, Agent, null!, Now));
    }

    [Fact]
    public void Verify_FieldWithNewLine_ReturnsFalse()
    {
        const string newLineIp = "203.0.113.50\n";
        var signature = Base64Url.EncodeToString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(Key),
                Encoding.UTF8.GetBytes($"v1\nclient-address\n{Scope}\n{UnixSeconds}\n{newLineIp}\n")));

        Assert.False(VerifyFor(signature, clientIp: newLineIp));
    }

    [Theory]
    [InlineData("client\naddress", Scope, ClientIp, Agent)]
    [InlineData(WebGatewayPurposes.ClientAddress, "scope\nvalue", ClientIp, Agent)]
    [InlineData(WebGatewayPurposes.ClientAddress, Scope, "203.0.113.50\n203.0.113.51", Agent)]
    [InlineData(WebGatewayPurposes.ClientAddress, Scope, ClientIp, "QnJv\nZA")]
    public void Sign_FieldWithNewLine_Throws(string purpose, string scope, string clientIp, string agent)
    {
        Assert.Throws<ArgumentException>(() => WebGatewaySignature.Sign(Key, purpose, scope, UnixSeconds, clientIp, agent));
    }

    private static string SignFor(
        string key = Key,
        string purpose = WebGatewayPurposes.ClientAddress,
        string scope = Scope,
        long unixSeconds = UnixSeconds,
        string clientIp = ClientIp,
        string agent = Agent) =>
        WebGatewaySignature.Sign(key, purpose, scope, unixSeconds, clientIp, agent);

    private static bool VerifyFor(
        string signature,
        string key = Key,
        string purpose = WebGatewayPurposes.ClientAddress,
        string scope = Scope,
        long unixSeconds = UnixSeconds,
        string clientIp = ClientIp,
        string agent = Agent,
        DateTimeOffset? now = null) =>
        WebGatewaySignature.Verify(key, purpose, scope, unixSeconds, clientIp, agent, signature, now ?? Now);
}
