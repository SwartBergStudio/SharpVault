// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.IntegrationTests.Factories;

public sealed class WebTestFactory : WebApplicationFactory<WebApp::Program>
{
    private ApiTestFactory? _api;

    public CapturingLoggerProvider Logs { get; } = new();

    public void UseApi(ApiTestFactory api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
    }

    public HttpClient CreateWebClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    public Task<HttpClient> CreateSignedInClientAsync(TestUser user, CancellationToken cancellationToken) =>
        WebSignIn.CreateClientAsync(this, user, cancellationToken);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        TestIdentityProvider.ApplyWebSettings(builder);
        builder.UseSetting("Logging:LogLevel:Default", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug");
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(Logs);
        });
        builder.ConfigureTestServices(services =>
        {
            TestIdentityProvider.ConfigureWeb(services);
            services.AddSingleton<IStartupFilter, TestCookieSignInStartupFilter>();
            services.AddSingleton(FakeTokenAcquisition.Create());
            if (_api is not null)
            {
                var api = _api;
                services.AddHttpClient<ISharpVaultApiClient, SharpVaultApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => api.Server.CreateHandler());
            }
        });
    }
}
