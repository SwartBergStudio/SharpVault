// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpVault.IntegrationTests.Logging;

namespace SharpVault.IntegrationTests.Factories;

public sealed class WebTestFactory : WebApplicationFactory<WebApp::Program>
{
    public CapturingLoggerProvider Logs { get; } = new();

    public HttpClient CreateWebClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Logging:LogLevel:Default", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug");
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(Logs);
        });
    }
}
