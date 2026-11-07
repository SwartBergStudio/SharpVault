// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
public sealed class TableStorageStartupTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly string[] ProbeTables = ["FoundationProbeOne", "FoundationProbeTwo"];

    [Fact]
    public async Task Startup_TableSetConfigured_CreatesEveryTableBeforeFirstRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var derived = CreateFactoryWithProbeTables();

        using var client = derived.CreateClient();

        var names = new List<string>();
        await foreach (var table in Factory.StorageAdminClient.QueryAsync(cancellationToken: cancellationToken))
        {
            names.Add(table.Name);
        }

        Assert.Contains("FoundationProbeOne", names);
        Assert.Contains("FoundationProbeTwo", names);
    }

    [Fact]
    public void Startup_TablesMissing_ListsOnceAndCreatesEach()
    {
        var names = FreshProbeNames();
        StorageRequests.Clear();

        using var derived = CreateFactoryWithProbeTables(names);
        using var client = derived.CreateClient();

        Assert.Equal(1 + names.Length, StorageRequests.Count);
        Assert.Equal(1, StorageRequests.Requests.Count(request => request.Method == "GET"));
        Assert.Equal(names.Length, StorageRequests.Requests.Count(request => request.Method == "POST"));
        Assert.All(StorageRequests.Requests, request => Assert.False(request.IsEntityQuery));
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public void Startup_TablesAlreadyExist_ListsOnceAndCreatesNone()
    {
        var names = FreshProbeNames();
        using (var first = CreateFactoryWithProbeTables(names))
        using (var firstClient = first.CreateClient())
        {
        }

        StorageRequests.Clear();
        using var second = CreateFactoryWithProbeTables(names);
        using var secondClient = second.CreateClient();

        Assert.Equal(1, StorageRequests.Count);
        Assert.Equal(1, StorageRequests.Requests.Count(request => request.Method == "GET"));
        Assert.DoesNotContain(StorageRequests.Requests, request => request.Method == "POST");
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task Startup_RequestsAfterStart_MakeNoFurtherTableCreationCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var derived = CreateFactoryWithProbeTables();
        using var client = derived.CreateClient();
        StorageRequests.Clear();

        for (var i = 0; i < 10; i++)
        {
            await client.GetAsync("/alive", cancellationToken);
            await client.GetAsync("/does-not-exist", cancellationToken);
        }

        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public void Startup_TableSetAlreadyExists_IsIdempotent()
    {
        using var first = CreateFactoryWithProbeTables();
        using var firstClient = first.CreateClient();

        using var second = CreateFactoryWithProbeTables();
        var exception = Record.Exception(() => second.CreateClient());

        Assert.Null(exception);
    }

    [Fact]
    public void Startup_DefaultTableSet_ListsOnceAndCreatesOnlyMissingTables()
    {
        StorageRequests.Clear();

        using var derived = Factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();

        Assert.Equal(1, StorageRequests.Requests.Count(request => request.Method == "GET"));
        Assert.All(StorageRequests.Requests.Where(request => request.Method == "POST"), request => Assert.False(request.IsEntityQuery));
        Assert.True(StorageRequests.Requests.Count(request => request.Method == "POST") <= TableNames.All.Count);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public void Startup_TableNamesAll_ContainsTheNineTablesInOrder()
    {
        Assert.Equal([TableNames.Users, TableNames.AuditLog, TableNames.Vaults, TableNames.SecretAuditLog, TableNames.Maintenance, TableNames.ShareInbox, TableNames.TransferJobs, TableNames.SessionRevocations, TableNames.OneTimeLinks], TableNames.All);
        Assert.Equal(["Users", "AuditLog", "Vaults", "SecretAuditLog", "Maintenance", "ShareInbox", "TransferJobs", "SessionRevocations", "OneTimeLinks"], TableNames.All);
    }

    [Fact]
    public async Task Startup_DefaultTableSet_CreatesAllNineTables()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await foreach (var table in Factory.StorageAdminClient.QueryAsync(cancellationToken: cancellationToken))
        {
            await Factory.StorageAdminClient.DeleteTableAsync(table.Name, cancellationToken);
        }

        using var derived = Factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();

        var names = new List<string>();
        await foreach (var table in Factory.StorageAdminClient.QueryAsync(cancellationToken: cancellationToken))
        {
            names.Add(table.Name);
        }

        Assert.All(TableNames.All, name => Assert.Contains(name, names));
    }

    private static string[] FreshProbeNames() => [$"ProbeA{Guid.NewGuid():N}", $"ProbeB{Guid.NewGuid():N}"];

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactoryWithProbeTables(string[]? names = null) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(new TableStorageTableSet(names ?? ProbeTables))));
}
