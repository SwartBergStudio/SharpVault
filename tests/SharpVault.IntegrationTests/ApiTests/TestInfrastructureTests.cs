// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
public sealed class TestInfrastructureTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ResetStorage_AfterTableCreated_RemovesTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Reset" + Guid.NewGuid().ToString("N")[..12];
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);

        await Factory.ResetStorageAsync(cancellationToken);

        var remaining = new List<string>();
        await foreach (var table in Factory.StorageAdminClient.QueryAsync(cancellationToken: cancellationToken))
        {
            remaining.Add(table.Name);
        }

        Assert.DoesNotContain(tableName, remaining);
    }

    [Fact]
    public async Task ResetStorage_RunTwice_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await Factory.ResetStorageAsync(cancellationToken);
        var exception = await Record.ExceptionAsync(() => Factory.ResetStorageAsync(cancellationToken));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StorageRequestRecorder_QueryWithoutPartitionKey_IsReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Scan" + Guid.NewGuid().ToString("N")[..12];
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        await foreach (var entity in appClient.GetTableClient(tableName).QueryAsync<TableEntity>("RowKey eq 'x'", cancellationToken: cancellationToken))
        {
            Assert.NotNull(entity);
        }

        var scans = StorageRequests.QueriesWithoutPartitionKey();
        var scan = Assert.Single(scans);
        Assert.True(scan.IsEntityQuery);
        Assert.False(scan.HasPartitionKeyCondition);
        Assert.Contains(tableName, scan.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StorageRequestRecorder_QueryWithPartitionKey_IsNotReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Keyed" + Guid.NewGuid().ToString("N")[..12];
        var partition = Fixture.Create<Guid>().ToString();
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        await foreach (var entity in appClient.GetTableClient(tableName).QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
        {
            Assert.NotNull(entity);
        }

        var recorded = Assert.Single(StorageRequests.Requests);
        Assert.True(recorded.IsEntityQuery);
        Assert.True(recorded.HasPartitionKeyCondition);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task StorageRequestRecorder_FilterContainingPartitionKeyOnlyInValue_IsReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Tricky" + Guid.NewGuid().ToString("N")[..12];
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        await foreach (var entity in appClient.GetTableClient(tableName).QueryAsync<TableEntity>("PartitionKey ne 'a'", cancellationToken: cancellationToken))
        {
            Assert.NotNull(entity);
        }

        Assert.Single(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task StorageRequestRecorder_TableCreation_IsRecordedButNotAnEntityQuery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Create" + Guid.NewGuid().ToString("N")[..12];
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        await appClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);

        var recorded = Assert.Single(StorageRequests.Requests);
        Assert.Equal("POST", recorded.Method);
        Assert.False(recorded.IsEntityQuery);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public void CapturingLoggerProvider_AppLogsMessage_IsCaptured()
    {
        var marker = "probe-" + Fixture.Create<Guid>();
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntegrationProbe");

        ProbeLog.Information(logger, marker);

        Assert.True(Logs.ContainsText(marker));
    }

    [Fact]
    public void CapturingLoggerProvider_ExceptionTextLogged_IsSearchable()
    {
        var marker = "boom-" + Fixture.Create<Guid>();
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntegrationProbe");

        ProbeLog.Failure(logger, new InvalidOperationException(marker));

        Assert.True(Logs.ContainsText(marker));
    }

    [Fact]
    public void CapturingLoggerProvider_Clear_RemovesEntries()
    {
        var marker = "cleared-" + Fixture.Create<Guid>();
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntegrationProbe");
        ProbeLog.Information(logger, marker);

        Logs.Clear();

        Assert.False(Logs.ContainsText(marker));
    }

    [Fact]
    public async Task TestAuthenticationHandler_NoHeaders_LeavesCallerAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TestAuthenticationHandler_ObjectIdHeader_AuthenticatesCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TestAuthenticationHandler_BlankObjectIdHeader_LeavesCallerAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Add(TestAuthenticationHandler.ObjectIdHeader, " ");

        var response = await Client.SendAsync(request, cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void ForgedTokens_AlgNone_HasThreeSegmentsAndEmptySignature()
    {
        var token = ForgedTokens.AlgNone();

        var segments = token.Split('.');
        Assert.Equal(3, segments.Length);
        Assert.Equal(string.Empty, segments[2]);
    }

    [Fact]
    public void ForgedTokens_SignedWithUnknownKey_DiffersBetweenCalls()
    {
        Assert.NotEqual(ForgedTokens.SignedWithUnknownKey(), ForgedTokens.SignedWithUnknownKey());
    }
}
