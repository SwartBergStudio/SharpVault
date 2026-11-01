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
    public void Startup_TableSetConfigured_MakesOneStorageCallPerTable()
    {
        StorageRequests.Clear();

        using var derived = CreateFactoryWithProbeTables();
        using var client = derived.CreateClient();

        Assert.Equal(2, StorageRequests.Count);
        Assert.All(StorageRequests.Requests, request =>
        {
            Assert.Equal("POST", request.Method);
            Assert.False(request.IsEntityQuery);
        });
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
    public void Startup_DefaultTableSet_MakesOneStorageCallPerTable()
    {
        StorageRequests.Clear();

        using var derived = Factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();

        Assert.Equal(TableNames.All.Count, StorageRequests.Count);
        Assert.All(StorageRequests.Requests, request =>
        {
            Assert.Equal("POST", request.Method);
            Assert.False(request.IsEntityQuery);
        });
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public void Startup_TableNamesAll_ContainsUsersAndAuditLogInOrder()
    {
        Assert.Equal([TableNames.Users, TableNames.AuditLog], TableNames.All);
        Assert.Equal(["Users", "AuditLog"], TableNames.All);
    }

    [Fact]
    public async Task Startup_DefaultTableSet_CreatesUsersAndAuditLogTables()
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

        Assert.Contains(TableNames.Users, names);
        Assert.Contains(TableNames.AuditLog, names);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactoryWithProbeTables() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(new TableStorageTableSet(ProbeTables))));
}
