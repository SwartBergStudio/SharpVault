// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(PerformanceTestGroup))]
[Trait("Category", "Performance")]
public sealed class PurgeGrantCascadePerformanceTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const int GrantCount = 100;
    private const int VersionCount = 2;
    private const int PointReadsPerGrant = 1;
    private const int InboxRequestsPerGrant = 2;
    private const int BudgetMilliseconds = 500;
    private const int ExpectedWriteTransactions = 3;

    private readonly Guid _owner = Guid.NewGuid();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private PurgeGrantWorld World
    {
        get
        {
            _ = Factory.Server;
            return new PurgeGrantWorld(
                Factory.Services.GetRequiredService<IVaultStore>(),
                Factory.Services.GetRequiredService<IShareInboxStore>(),
                GetTableClient(TableNames.Vaults),
                GetTableClient(TableNames.ShareInbox),
                _owner);
        }
    }

    private int InboxRequests => StorageRequests.Requests.Count(request => request.Path.Contains(TableNames.ShareInbox, StringComparison.Ordinal));

    private IReadOnlyList<RecordedStorageRequest> VaultReads => StorageRequests.VaultReads();

    [Fact]
    public async Task EraseSecret_100Grants_MakesOnePointReadPerGrantPlusTwoQueriesAndThreeWriteTransactions()
    {
        var world = World;
        var (binned, _) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);
        StorageRequests.Clear();

        await world.Eraser.EraseSecretAsync(_owner, binned, Token);

        var reads = VaultReads;
        Assert.Equal(GrantCount * PointReadsPerGrant, reads.Count(request => !request.IsEntityQuery));
        Assert.Equal(2, reads.Count(request => request.IsEntityQuery));
        Assert.Equal(ExpectedWriteTransactions, StorageRequests.VaultWrites().Count);
        Assert.Equal(GrantCount * InboxRequestsPerGrant, InboxRequests);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task EraseSecret_NoGrants_MakesOnlyTheVersionsQueryAndOneWriteTransaction()
    {
        var world = World;
        var (binned, _) = await world.SeedRecycledSecretAsync(0, VersionCount, Token);
        StorageRequests.Clear();

        await world.Eraser.EraseSecretAsync(_owner, binned, Token);

        Assert.Equal(2, VaultReads.Count);
        Assert.All(VaultReads, request => Assert.True(request.IsEntityQuery));
        Assert.Single(StorageRequests.VaultWrites());
        Assert.Equal(0, InboxRequests);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task EraseSecret_100Grants_Under500ms()
    {
        var world = World;
        var (binned, _) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);
        var warmup = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);
        await world.Eraser.EraseSecretAsync(_owner, warmup.Binned, Token);
        StorageRequests.Clear();

        var clock = Stopwatch.StartNew();
        var erased = await world.Eraser.EraseSecretAsync(_owner, binned, Token);
        clock.Stop();

        Assert.Equal(StoreWriteResult.Written, erased.Write.Result);
        Assert.True(clock.ElapsedMilliseconds < BudgetMilliseconds, $"Purging a secret with {GrantCount} grants took {clock.ElapsedMilliseconds} ms.");
    }
}
