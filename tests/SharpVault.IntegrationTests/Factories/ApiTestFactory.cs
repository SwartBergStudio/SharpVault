// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Storage;
using Testcontainers.Azurite;

namespace SharpVault.IntegrationTests.Factories;

public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(TestContainerImages.Azurite)
        .WithInMemoryPersistence()
        .Build();

    private TableServiceClient? _storageAdminClient;

    public StorageRequestRecorder StorageRequests { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public string AzuriteConnectionString => _azurite.GetConnectionString();

    public TableServiceClient StorageAdminClient =>
        _storageAdminClient ?? throw new InvalidOperationException("The factory has not been initialised.");

    public async ValueTask InitializeAsync()
    {
        await _azurite.StartAsync(TestContext.Current.CancellationToken);
        _storageAdminClient = new TableServiceClient(_azurite.GetConnectionString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    public HttpClient CreateAuthenticatedClient(TestUser user)
    {
        var client = CreateUnauthenticatedClient();
        TestAuthenticationHandler.AddHeaders(client, user);
        return client;
    }

    public async Task ResetStorageAsync(CancellationToken cancellationToken)
    {
        await foreach (var table in StorageAdminClient.QueryAsync(cancellationToken: cancellationToken))
        {
            await StorageAdminClient.DeleteTableAsync(table.Name, cancellationToken);
        }

        var tableSet = Services.GetRequiredService<TableStorageTableSet>();
        foreach (var name in tableSet.Names)
        {
            await StorageAdminClient.CreateTableIfNotExistsAsync(name, cancellationToken);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting($"ConnectionStrings:{TableStorageConnection.Name}", _azurite.GetConnectionString());
        builder.UseSetting("Logging:LogLevel:Default", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug");
        builder.UseSetting("Logging:LogLevel:System", "Debug");
        builder.UseSetting("Logging:LogLevel:Azure", "Debug");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<HttpPipelinePolicy>(StorageRequests);
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        });
    }
}
