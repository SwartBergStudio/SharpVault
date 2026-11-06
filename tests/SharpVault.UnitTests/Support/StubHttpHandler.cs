// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text;

namespace SharpVault.UnitTests.Support;

internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private HttpStatusCode _status = HttpStatusCode.OK;
    private string _body = string.Empty;
    private string _contentType = "application/json";
    private Exception? _failure;

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public void Respond(HttpStatusCode status, string body, string contentType = "application/json")
    {
        _status = status;
        _body = body;
        _contentType = contentType;
        _failure = null;
    }

    public void Throw(Exception failure) => _failure = failure;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Add(new RecordedRequest(
            request.RequestUri!,
            request.Method,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            request.Headers.TryGetValues("If-Match", out var ifMatch) ? string.Join(',', ifMatch) : null,
            body,
            request.Content?.Headers.ContentType?.MediaType));

        if (_failure is not null)
        {
            throw _failure;
        }

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, _contentType),
        };
    }

    internal sealed record RecordedRequest(Uri Uri, HttpMethod Method, string? AuthorizationScheme, string? AuthorizationParameter, string? IfMatch = null, string? Body = null, string? ContentType = null);
}
