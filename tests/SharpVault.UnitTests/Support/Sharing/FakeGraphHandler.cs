// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text;

namespace SharpVault.UnitTests.Support.Sharing;

internal sealed class FakeGraphHandler : HttpMessageHandler
{
    private readonly List<GraphRequest> _requests = [];
    private HttpStatusCode _status = HttpStatusCode.OK;
    private string _body = "{\"value\":[]}";

    public IReadOnlyList<GraphRequest> Requests => _requests;

    public void Respond(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Add(new GraphRequest(
            request.RequestUri!,
            request.Method,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            request.Headers.TryGetValues("ConsistencyLevel", out var level) ? string.Join(',', level) : null));

        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        });
    }

    internal sealed record GraphRequest(
        Uri Uri,
        HttpMethod Method,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? ConsistencyLevel);
}
