// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.IntegrationTests.Support;

public static class TestWebGateway
{
    public const string Key = "test-web-gateway-key-0123456789abcdefghi";

    public static void AddProof(
        HttpRequestMessage request,
        Guid userId,
        string clientIp,
        DateTimeOffset? at = null,
        string? key = null,
        string purpose = WebGatewayPurposes.ClientAddress)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unixSeconds = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var signature = WebGatewaySignature.Sign(
            key ?? Key,
            purpose,
            WebGatewayScope.For(userId),
            unixSeconds,
            clientIp,
            string.Empty);

        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Time, unixSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, clientIp);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Signature, signature);
    }

    public static void AddLinkOpenProof(
        HttpRequestMessage request,
        string path,
        string clientIp,
        string agent,
        DateTimeOffset? at = null,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unixSeconds = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var agentBase64Url = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(agent));
        var signature = WebGatewaySignature.Sign(
            key ?? Key,
            WebGatewayPurposes.LinkOpen,
            WebGatewayScope.ForRoute(request.Method.Method, path),
            unixSeconds,
            clientIp,
            agentBase64Url);

        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Time, unixSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, clientIp);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientAgent, agentBase64Url);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Signature, signature);
    }
}
