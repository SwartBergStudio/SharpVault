// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Azure.Data.Tables;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Performance")]
public sealed class UserCountsPerformanceTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const int SecretRows = 5000;
    private const int GrantRows = 100;
    private const int TransactionSize = 100;
    private const int MaxStorageCalls = 9;
    private const int BudgetMilliseconds = 1000;
    private const string GuidFormat = "N";

    [Fact]
    public async Task GetUserCounts_5000SecretsAnd100Grants_StaysInBudget()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.User);
        await SeedRowsAsync(TestUsers.User.ObjectId);
        using var client = ClientFor(TestUsers.Admin);
        Factory.StorageRequests.Clear();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync(CountsPath(TestUsers.User.ObjectId), Token);
        stopwatch.Stop();
        var requests = Factory.StorageRequests.Requests;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<UserCountsDto>(Token);
        Assert.NotNull(counts);
        Assert.Equal(SecretRows, counts.ItemCount);
        Assert.Equal(GrantRows, counts.ShareCount);
        Assert.False(counts.CountsAreCapped);
        Assert.True(stopwatch.ElapsedMilliseconds < BudgetMilliseconds, $"The counts took {stopwatch.ElapsedMilliseconds} ms, over the {BudgetMilliseconds} ms budget.");
        Assert.True(requests.Count <= MaxStorageCalls, $"The counts made {requests.Count} storage calls, over the budget of {MaxStorageCalls}.");
        Assert.Empty(Factory.StorageRequests.QueriesWithoutPartitionKey());
        Assert.DoesNotContain(requests, request => request.Path.Contains($"RowKey='{VaultKeys.KeyRingRowKey}'", StringComparison.Ordinal));
    }

    private async Task SeedRowsAsync(Guid owner)
    {
        var partitionKey = VaultKeys.PartitionKey(owner);
        var createdAt = DateTimeOffset.UtcNow;
        var secretActions = Enumerable.Range(0, SecretRows)
            .Select(_ => new TableEntity(partitionKey, VaultKeys.SecretPrefix + Guid.NewGuid().ToString(GuidFormat, CultureInfo.InvariantCulture))
            {
                ["FolderKey"] = VaultKeys.RootFolderKey,
                ["CreatedAt"] = createdAt,
            })
            .Select(entity => new TableTransactionAction(TableTransactionActionType.Add, entity));
        var grantActions = Enumerable.Range(0, GrantRows)
            .Select(_ => new TableEntity(partitionKey, VaultKeys.GrantByItemPrefix + Guid.NewGuid().ToString(GuidFormat, CultureInfo.InvariantCulture)))
            .Select(entity => new TableTransactionAction(TableTransactionActionType.Add, entity));

        foreach (var batch in secretActions.Concat(grantActions).Chunk(TransactionSize))
        {
            await VaultsTable.SubmitTransactionAsync(batch, Token);
        }
    }

    private static string CountsPath(Guid userId) => ApiRoutes.AdminUserCounts.Replace("{userId:guid}", userId.ToString("D"), StringComparison.Ordinal);
}
