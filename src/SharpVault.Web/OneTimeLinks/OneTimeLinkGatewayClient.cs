// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpVault.Authentication.WebGateway;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Web.OneTimeLinks;

public sealed partial class OneTimeLinkGatewayClient(
    HttpClient httpClient,
    IOptions<WebGatewayOptions> options,
    TimeProvider timeProvider,
    ILogger<OneTimeLinkGatewayClient> logger) : IOneTimeLinkGatewayClient
{
    public async Task<OneTimeLinkOpenResult> OpenAsync(Guid linkId, string clientIp, string userAgent, CancellationToken cancellationToken)
    {
        var key = options.Value.NormalizedKey;
        if (key is null)
        {
            LogKeyMissing(logger);
            return OneTimeLinkOpenResult.Unavailable;
        }

        try
        {
            using var request = BuildRequest(key, linkId, clientIp, userAgent);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return await ReadAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogOpenFailed(logger, exception.GetType().Name);
            return OneTimeLinkOpenResult.Unavailable;
        }
    }

    private HttpRequestMessage BuildRequest(string key, Guid linkId, string clientIp, string userAgent)
    {
        var path = OneTimeLinkRelayRules.OpenPath(linkId);
        var unixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var agent = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(userAgent));
        var signature = WebGatewaySignature.Sign(
            key,
            WebGatewayPurposes.LinkOpen,
            WebGatewayScope.ForRoute(HttpMethod.Post.Method, path),
            unixSeconds,
            clientIp,
            agent);

        var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'));
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Time, unixSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, clientIp);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientAgent, agent);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Signature, signature);
        return request;
    }

    private async Task<OneTimeLinkOpenResult> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<OneTimeLinkPayloadDto>(JsonSerializerOptions.Web, cancellationToken);
            return payload is null
                ? OneTimeLinkOpenResult.Unavailable
                : new OneTimeLinkOpenResult(OneTimeLinkOpenOutcome.Opened, payload);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new OneTimeLinkOpenResult(OneTimeLinkOpenOutcome.RateLimited, RetryAfter: response.Headers.RetryAfter?.Delta);
        }

        LogUnexpectedStatus(logger, (int)response.StatusCode);
        return OneTimeLinkOpenResult.Unavailable;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "One-time link open skipped: WebGateway:Key is not set")]
    private static partial void LogKeyMissing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "One-time link open returned status {StatusCode}")]
    private static partial void LogUnexpectedStatus(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "One-time link open failed ({ExceptionType})")]
    private static partial void LogOpenFailed(ILogger logger, string exceptionType);
}
