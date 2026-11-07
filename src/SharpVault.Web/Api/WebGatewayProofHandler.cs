// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using SharpVault.Authentication.WebGateway;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.Web.Security;

namespace SharpVault.Web.Api;

public sealed partial class WebGatewayProofHandler(
    IOptions<WebGatewayOptions> options,
    TimeProvider timeProvider,
    ILogger<WebGatewayProofHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RemoveProofHeaders(request.Headers);
        if (options.Value.NormalizedKey is { } key)
        {
            SignRequest(request, key);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private void SignRequest(HttpRequestMessage request, string key)
    {
        if (!request.Options.TryGetValue(ApiRequestOptions.Principal, out var principal)
            || !WebGatewayScope.TryReadUserId(principal, out var userId))
        {
            LogNoSignedInPrincipal(logger);
            return;
        }

        var address = ClientAddressClaim.Read(principal) ?? string.Empty;
        if (address.Length == 0)
        {
            LogNoBrowserAddress(logger);
        }

        var unixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebGatewaySignature.Sign(
            key,
            WebGatewayPurposes.ClientAddress,
            WebGatewayScope.For(userId),
            unixSeconds,
            address,
            string.Empty);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Time, unixSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, address);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Signature, signature);
    }

    private static void RemoveProofHeaders(HttpRequestHeaders headers)
    {
        headers.Remove(WebGatewayHeaders.Time);
        headers.Remove(WebGatewayHeaders.ClientIp);
        headers.Remove(WebGatewayHeaders.Signature);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The API call has no signed-in principal, so it carries no address proof")]
    private static partial void LogNoSignedInPrincipal(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browser address is unknown for this API call, so its audit entry holds no address")]
    private static partial void LogNoBrowserAddress(ILogger logger);
}
