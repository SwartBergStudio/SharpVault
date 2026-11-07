// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net.Http.Headers;
using System.Security.Cryptography;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using SharpVault.Api.Security;
using SharpVault.Authentication.Local;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using Testcontainers.Azurite;

namespace SharpVault.IntegrationTests.Factories;

public sealed class LocalApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(TestContainerImages.Azurite)
        .WithInMemoryPersistence()
        .Build();

    private TableServiceClient? _storageAdminClient;

    public StorageRequestRecorder StorageRequests { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public OpenTelemetryLogRecorder OpenTelemetryLogs { get; } = new();

    public string DevelopmentKeyMaterial { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

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

    public static HttpClient CreateClientFor(WebApplicationFactory<Program> source, string? token = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var client = source.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public HttpClient CreateUnauthenticatedClient() => CreateClientFor(this);

    public HttpClient CreateClientForAccount(LocalAccount account) => CreateClientFor(this, LocalTokens.For(Services, account));

    public HttpClient CreateClientWithToken(string token) => CreateClientFor(this, token);

    public async Task ResetAndSeedAsync(CancellationToken cancellationToken)
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

        await RunSeederAsync(cancellationToken);
    }

    public Task RunSeederAsync(CancellationToken cancellationToken)
    {
        var seeder = Services.GetServices<IHostedService>().OfType<LocalAccountSeeder>().Single();
        return seeder.StartAsync(cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting($"ConnectionStrings:{TableStorageConnection.Name}", _azurite.GetConnectionString());
        builder.UseSetting("AzureAd:TenantId", string.Empty);
        builder.UseSetting("AzureAd:ClientId", string.Empty);
        builder.UseSetting("LocalAuth:KeyFilePath", LocalKeyFile.Path);
        builder.UseSetting("UserAccess:CacheTtl", "00:00:00");
        builder.UseSetting("RecycleBinPurge:Enabled", "false");
        builder.UseSetting("Transfers:Enabled", "false");
        builder.UseSetting("VaultRateLimits:RequestsPerMinute", "100000");
        builder.UseSetting("VaultRateLimits:RevealsPerMinute", "100000");
        builder.UseSetting("VaultRateLimits:ShareWritesPerMinute", "100000");
        builder.UseSetting("VaultRateLimits:DirectorySearchesPerMinute", "100000");
        builder.UseSetting("VaultRateLimits:AdminActionsPerMinute", "100000");
        builder.UseSetting($"{EncryptionOptions.SectionName}:{nameof(EncryptionOptions.DevelopmentKeyMaterial)}", DevelopmentKeyMaterial);
        builder.UseSetting("WebGateway:Key", TestWebGateway.Key);
        builder.UseSetting("OneTimeLinks:Enabled", "true");
        builder.UseSetting("OneTimeLinks:PurgeEnabled", "false");
        builder.UseSetting("VaultRateLimits:OneTimeLinkOpensPerMinute", "100000");
        builder.UseSetting("Logging:LogLevel:Default", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Debug");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug");
        builder.UseSetting("Logging:LogLevel:System", "Debug");
        builder.UseSetting("Logging:LogLevel:Azure", "Debug");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<HttpPipelinePolicy>(StorageRequests);
            services.Configure<OpenTelemetryLoggerOptions>(options => options.AddProcessor(OpenTelemetryLogs));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        EntraModeAssertion.AssertLocal(host.Services);
        return host;
    }
}
