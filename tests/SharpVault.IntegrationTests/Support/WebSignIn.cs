// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.AspNetCore.Mvc.Testing;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Support;

public static class WebSignIn
{
    public static async Task<HttpClient> CreateClientAsync(
        WebApplicationFactory<WebApp::Program> factory,
        TestUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(user);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        var response = await client.GetAsync($"{TestCookieSignInStartupFilter.Path}/{user.ObjectId:D}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return client;
    }
}
