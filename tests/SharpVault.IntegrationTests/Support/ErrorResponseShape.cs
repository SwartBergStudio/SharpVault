// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;

namespace SharpVault.IntegrationTests.Support;

public sealed record ErrorResponseShape(
    HttpStatusCode Status,
    string? ContentType,
    string HeaderNames,
    string? Challenge,
    string BodyShape)
{
    private static readonly string[] VolatileHeaders = ["date", "server", "content-length", "traceparent", "request-id"];
    private static readonly string[] VolatileProperties = ["traceId"];

    public static async Task<ErrorResponseShape> CaptureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var headerNames = response.Headers.Select(header => header.Key)
            .Concat(response.Content.Headers.Select(header => header.Key))
            .Select(name => name.ToLowerInvariant())
            .Where(name => !VolatileHeaders.Contains(name))
            .Order(StringComparer.Ordinal);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ErrorResponseShape(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            string.Join(',', headerNames),
            response.HeaderValue("WWW-Authenticate"),
            Normalize(body));
    }

    private static string Normalize(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return body;
        }

        return string.Join(
            '|',
            document.RootElement.EnumerateObject()
                .Where(property => !VolatileProperties.Contains(property.Name))
                .Select(property => $"{property.Name}={property.Value.GetRawText()}")
                .Order(StringComparer.Ordinal));
    }
}
