// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Azure.Core.Pipeline;
using System.Net.Http.Headers;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Directory;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Encryption;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Support;
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

    public TestDirectoryHandler GraphDirectory { get; } = new();

    public CountingKeyWrapProvider KeyWraps => Services.GetRequiredService<CountingKeyWrapProvider>();

    public string DevelopmentKeyMaterial { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

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
        ArgumentNullException.ThrowIfNull(user);

        return CreateClientWithToken(TestTokens.Create(user));
    }

    public HttpClient CreateClientWithToken(string token)
    {
        var client = CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task SeedUserAsync(TestUser user, string status, DateTimeOffset lastSignInAt, CancellationToken cancellationToken)
    {
        await StorageAdminClient.CreateTableIfNotExistsAsync(TableNames.Users, cancellationToken);
        await UserRows.InsertAsync(StorageAdminClient.GetTableClient(TableNames.Users), user, status, lastSignInAt, cancellationToken);
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
        TestIdentityProvider.ApplyApiSettings(builder);
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
            TestIdentityProvider.ConfigureApi(services);
            services.RemoveAll<GraphCredential>();
            services.AddSingleton(new GraphCredential(new TestGraphCredential()));
            services.AddHttpClient<IUserDirectory, GraphUserDirectory>().ConfigurePrimaryHttpMessageHandler(() => GraphDirectory);
            services.RemoveAll<IEnvelopeEncryptor>();
            services.AddSingleton(provider => new CountingKeyWrapProvider(provider.GetRequiredService<IKeyWrapProvider>()));
            services.AddSingleton<IEnvelopeEncryptor>(provider => new EnvelopeEncryptor(provider.GetRequiredService<CountingKeyWrapProvider>(), provider.GetRequiredService<ILogger<EnvelopeEncryptor>>()));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        EntraModeAssertion.Assert(host.Services);
        return host;
    }
}
