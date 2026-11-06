// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Support;

public sealed class NoApiAttachedHandler : HttpMessageHandler
{
    public const string Message = "The Web host was built before UseApi attached an API, so its API calls would leave the process. Call UseApi before the first use of Services or CreateWebClient.";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);
}
