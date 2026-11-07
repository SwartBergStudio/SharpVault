// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.Api.Security;

public sealed class LinkGatewayAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<WebGatewayOptions> gatewayOptions,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = gatewayOptions.Value.NormalizedKey;
        if (key is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var proof = ReadProof(Request.Headers);
        if (proof is null || !IsSigned(key, proof))
        {
            return Task.FromResult(AuthenticateResult.Fail(LinkGatewayMessages.ProofRejected));
        }

        var ticket = BuildTicket(proof);
        return Task.FromResult(ticket is null
            ? AuthenticateResult.Fail(LinkGatewayMessages.ProofRejected)
            : AuthenticateResult.Success(ticket));
    }

    private bool IsSigned(string key, LinkOpenProof proof) =>
        WebGatewaySignature.Verify(
            key,
            WebGatewayPurposes.LinkOpen,
            WebGatewayScope.ForRoute(Request.Method, Request.Path.Value ?? string.Empty),
            proof.UnixSeconds,
            proof.ClientIp,
            proof.AgentBase64Url,
            proof.Signature,
            timeProvider.GetUtcNow());

    private AuthenticationTicket? BuildTicket(LinkOpenProof proof)
    {
        if (!TryNormalizeAddress(proof.ClientIp, out var address) || !TryDecodeText(proof.AgentBase64Url, out var agent))
        {
            return null;
        }

        var claims = new List<Claim>(2);
        if (address is not null)
        {
            claims.Add(new Claim(LinkGatewayClaims.ClientIp, address));
        }

        if (agent.Length > 0)
        {
            claims.Add(new Claim(LinkGatewayClaims.UserAgent, agent));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return new AuthenticationTicket(principal, Scheme.Name);
    }

    private static LinkOpenProof? ReadProof(IHeaderDictionary headers)
    {
        if (!TryGetSingle(headers, WebGatewayHeaders.Time, out var time)
            || !TryGetSingle(headers, WebGatewayHeaders.ClientIp, out var clientIp)
            || !TryGetSingle(headers, WebGatewayHeaders.ClientAgent, out var agent)
            || !TryGetSingle(headers, WebGatewayHeaders.Signature, out var signature)
            || !long.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return null;
        }

        return new LinkOpenProof(unixSeconds, clientIp, agent, signature);
    }

    private static bool TryGetSingle(IHeaderDictionary headers, string name, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(name, out var values) || values.Count != 1 || values[0] is not { } single)
        {
            return false;
        }

        value = single;
        return true;
    }

    private static bool TryNormalizeAddress(string raw, out string? address)
    {
        if (raw.Length == 0)
        {
            address = null;
            return true;
        }

        address = UserProfileText.NormalizeIpAddress(raw);
        return address is not null;
    }

    private static bool TryDecodeText(string base64Url, out string text)
    {
        text = string.Empty;
        if (!Base64Url.IsValid(base64Url))
        {
            return false;
        }

        text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(base64Url));
        return true;
    }

    private sealed record LinkOpenProof(long UnixSeconds, string ClientIp, string AgentBase64Url, string Signature);
}
