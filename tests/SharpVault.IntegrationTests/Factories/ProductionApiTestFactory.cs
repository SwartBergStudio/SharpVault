// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Logging;

namespace SharpVault.IntegrationTests.Factories;

public sealed class ProductionApiTestFactory : WebApplicationFactory<Program>
{
    public const string ServiceUri = "https://sharpvaultfoundationtest.table.core.windows.net/";

    public CapturingLoggerProvider Logs { get; } = new();

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting($"ConnectionStrings:{TableStorageConnection.Name}", ServiceUri);
        TestIdentityProvider.ApplyApiSettings(builder);
        builder.UseSetting("Logging:LogLevel:Default", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug");
        builder.UseSetting("Logging:LogLevel:System", "Debug");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            TestIdentityProvider.ConfigureApi(services);
            var initializer = services.FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(TableStorageInitializer));
            if (initializer is not null)
            {
                services.Remove(initializer);
            }
        });
    }
}
