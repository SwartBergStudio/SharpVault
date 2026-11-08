// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text.Json;
using SharpVault.Web.Security;

namespace SharpVault.Web.OneTimeLinks;

public static class OneTimeLinkEndpoints
{
    public static WebApplication MapOneTimeLinkOpen(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(WebRoutes.OneTimeLinkOpen, OpenAsync)
            .AllowAnonymous()
            .WithName("OneTimeLinks_Open");
        return app;
    }

    private static async Task<IResult> OpenAsync(string linkId, HttpContext context, IOneTimeLinkGatewayClient gateway, CancellationToken cancellationToken)
    {
        if (!IsRelayRequest(context.Request) || !Guid.TryParseExact(linkId, OneTimeLinkRelayRules.LinkIdFormat, out var id))
        {
            return Unavailable();
        }

        var result = await gateway.OpenAsync(id, ConnectionAddress(context), VisitorAgent(context), cancellationToken);
        return result.Outcome switch
        {
            OneTimeLinkOpenOutcome.Opened => Results.Json(result.Payload, JsonSerializerOptions.Web),
            OneTimeLinkOpenOutcome.RateLimited => RateLimited(context, result.RetryAfter),
            _ => Unavailable(),
        };
    }

    private static bool IsRelayRequest(HttpRequest request) =>
        string.Equals(
            request.Headers[OneTimeLinkRelayRules.LinkOpenHeader].ToString(),
            OneTimeLinkRelayRules.LinkOpenHeaderValue,
            StringComparison.Ordinal);

    private static string ConnectionAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address
            ? ClientAddressUnmapping.Unmap(address).ToString()
            : string.Empty;

    private static string VisitorAgent(HttpContext context)
    {
        var agent = context.Request.Headers.UserAgent.ToString();
        return agent.Length > OneTimeLinkRelayRules.UserAgentMaxLength
            ? agent[..OneTimeLinkRelayRules.UserAgentMaxLength]
            : agent;
    }

    private static IResult RateLimited(HttpContext context, TimeSpan? retryAfter)
    {
        if (retryAfter is { } delay)
        {
            context.Response.Headers.RetryAfter = Math.Ceiling(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return Results.Json(
            new OneTimeLinkOpenStatus(OneTimeLinkRelayRules.RateLimitedStatus),
            JsonSerializerOptions.Web,
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static IResult Unavailable() =>
        Results.Json(
            new OneTimeLinkOpenStatus(OneTimeLinkRelayRules.UnavailableStatus),
            JsonSerializerOptions.Web,
            statusCode: StatusCodes.Status404NotFound);
}
