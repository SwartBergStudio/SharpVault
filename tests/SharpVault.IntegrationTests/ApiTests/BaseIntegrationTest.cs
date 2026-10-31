// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Azure.Data.Tables;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests;

public abstract class BaseIntegrationTest(ApiTestFactory factory) : IAsyncLifetime
{
    protected ApiTestFactory Factory { get; } = factory;

    protected Fixture Fixture { get; } = new();

    protected HttpClient Client { get; private set; } = factory.CreateUnauthenticatedClient();

    protected StorageRequestRecorder StorageRequests => Factory.StorageRequests;

    protected CapturingLoggerProvider Logs => Factory.Logs;

    public ValueTask InitializeAsync()
    {
        StorageRequests.Clear();
        Logs.Clear();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.ResetStorageAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    protected void SetAuthenticatedClient(TestUser user) => Client = Factory.CreateAuthenticatedClient(user);

    protected HttpClient CreateUnauthenticatedClient() => Factory.CreateUnauthenticatedClient();

    protected async Task<HttpClient> ArrangeCallerAsync(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seededAt = DateTimeOffset.UtcNow;
        foreach (var locked in new[] { TestUsers.LockedUser, TestUsers.LockedAdmin })
        {
            if (await UserRows.GetAsync(GetUsersTable(), locked.ObjectId, cancellationToken) is null)
            {
                await Factory.SeedUserAsync(locked, UserRows.LockedStatus, seededAt, cancellationToken);
            }
        }

        return CallerClients.CreateFor(Factory, caller);
    }

    protected TableClient GetUsersTable() => GetTableClient(TableNames.Users);

    protected TableClient GetAuditTable() => GetTableClient(TableNames.AuditLog);

    protected void AssertNoQueryWithoutPartitionKey() => Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());

    protected TableClient GetTableClient(string tableName) => Factory.StorageAdminClient.GetTableClient(tableName);

    protected void AssertLogsDoNotContain(params string[] values)
    {
        foreach (var value in values)
        {
            var offending = Logs.Entries.FirstOrDefault(entry => entry.Message.Contains(value, StringComparison.Ordinal));
            Assert.False(
                Logs.ContainsText(value),
                $"A captured log entry contained a value that must never be logged (category '{offending?.Category}', level {offending?.Level}).");
        }
    }
}
