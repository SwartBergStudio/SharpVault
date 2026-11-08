// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using SharpVault.Dtos;

namespace SharpVault.IntegrationTests.Support;

internal sealed class ApiOpenFaultHandler(HttpMessageHandler inner, ApiOpenFault fault) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (fault.Status is not { } status || !IsOpenRoute(request.RequestUri))
        {
            return base.SendAsync(request, cancellationToken);
        }

        var response = new HttpResponseMessage(status);
        if (fault.RetryAfter is { } delay)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        }

        return Task.FromResult(response);
    }

    private static bool IsOpenRoute(Uri? uri) =>
        uri is not null
        && uri.AbsolutePath.StartsWith(ApiRoutes.OneTimeLinks + "/", StringComparison.Ordinal)
        && uri.AbsolutePath.EndsWith("/open", StringComparison.Ordinal);
}
