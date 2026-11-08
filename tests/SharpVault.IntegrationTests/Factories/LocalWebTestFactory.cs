// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Support;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.IntegrationTests.Factories;

public sealed class LocalWebTestFactory : WebApplicationFactory<WebApp::Program>
{
    private LocalApiTestFactory? _api;

    public CapturingLoggerProvider Logs { get; } = new();

    public OpenTelemetryLogRecorder OpenTelemetryLogs { get; } = new();

    public HttpMessageHandler? SharedSessionHandler { get; set; }

    public void UseApi(LocalApiTestFactory api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
    }

    public HttpClient CreateWebClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("AzureAd:TenantId", string.Empty);
        builder.UseSetting("AzureAd:ClientId", string.Empty);
        builder.UseSetting("LocalAuth:KeyFilePath", LocalKeyFile.Path);
        builder.UseSetting("SharpVaultApi:BaseAddress", "https://localhost/");
        builder.UseSetting("WebGateway:Key", TestWebGateway.Key);
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
            services.AddSingleton<IStartupFilter, LoopbackStartupFilter>();
            services.Configure<OpenTelemetryLoggerOptions>(options => options.AddProcessor(OpenTelemetryLogs));
            if (_api is not null)
            {
                var api = _api;
                services.UsePrimaryHandler(() => api.Server.CreateHandler(context => context.Connection.RemoteIpAddress = TestAddresses.WebHost));
                return;
            }

            services.UsePrimaryHandler(() => new NoApiAttachedHandler());
            if (SharedSessionHandler is { } sharedHandler)
            {
                services.UseSharedSessionHandler(() => sharedHandler);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        EntraModeAssertion.AssertLocal(host.Services);
        return host;
    }
}
