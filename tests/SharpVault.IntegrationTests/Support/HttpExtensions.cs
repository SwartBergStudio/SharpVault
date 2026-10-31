// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Support;

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> SendAsync(this HttpClient client, string method, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        return client.SendAsync(request, cancellationToken);
    }

    public static string? HeaderValue(this HttpResponseMessage response, string name)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
        {
            return string.Join(", ", values);
        }

        return null;
    }
}
