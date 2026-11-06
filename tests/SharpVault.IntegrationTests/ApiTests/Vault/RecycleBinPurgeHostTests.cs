// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed partial class RecycleBinPurgeHostTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);

    [GeneratedRegex(@"^/[^/]+/Maintenance(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex MaintenancePath();

    private IReadOnlyList<RecordedStorageRequest> MaintenanceCalls() =>
        [.. StorageRequests.Requests.Where(request => MaintenancePath().IsMatch(request.Path))];

    private static RecycleBinPurgeService PurgeServiceOf(WebApplicationFactory<Program> host) =>
        host.Services.GetServices<IHostedService>().OfType<RecycleBinPurgeService>().Single();

    private WebApplicationFactory<Program> WithPurge(bool enabled) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RecycleBinPurge:Enabled", enabled ? "true" : "false");
            builder.UseSetting("RecycleBinPurge:InitialDelay", "00:00:00");
            builder.UseSetting("RecycleBinPurge:Interval", "01:00:00");
        });

    [Fact]
    public async Task PurgeService_Disabled_MakesNoMaintenanceCalls()
    {
        StorageRequests.Clear();
        await using var host = WithPurge(enabled: false);
        _ = host.Server;
        var service = PurgeServiceOf(host);

        await service.ExecuteTask!.WaitAsync(PollTimeout, Token);

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
        Assert.Empty(MaintenanceCalls());
    }

    [Fact]
    public async Task PurgeService_Disabled_AfterServingRequests_StillMakesNoMaintenanceCalls()
    {
        StorageRequests.Clear();
        await using var host = WithPurge(enabled: false);
        using var client = CreateClientFor(host, Authentication.TestTokens.Create(Authentication.TestUsers.User));
        (await client.GetAsync("/api/me", Token)).EnsureSuccessStatusCode();
        await PurgeServiceOf(host).ExecuteTask!.WaitAsync(PollTimeout, Token);

        Assert.Empty(MaintenanceCalls());
    }

    [Fact]
    public async Task PurgeService_Enabled_TouchesTheMaintenanceTable()
    {
        StorageRequests.Clear();
        await using var host = WithPurge(enabled: true);
        _ = host.Server;

        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (MaintenanceCalls().Count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }

        Assert.NotEmpty(MaintenanceCalls());
        Assert.False(PurgeServiceOf(host).ExecuteTask!.IsCompleted);
    }

    [Fact]
    public async Task PurgeService_TestFactoryDefault_IsDisabled()
    {
        _ = Factory.Server;
        var service = PurgeServiceOf(Factory);

        await service.ExecuteTask!.WaitAsync(PollTimeout, Token);

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
    }
}
