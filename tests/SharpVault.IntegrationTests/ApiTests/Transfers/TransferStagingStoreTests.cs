// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferStagingStoreTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int OversizeValueBytes = 5 * 1024 * 1024;
    private const int SmallValueBytes = 64;
    private const int ManyItemCount = 150;

    [Fact]
    public async Task StageSecretsAsync_OversizedItem_IsReturnedAndNotStagedWhileTheRestStage()
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var transferId = Guid.NewGuid();
        var owner = TestUsers.OtherUser.ObjectId;
        var oversized = BuildSecret(owner, OversizeValueBytes);
        var healthy = BuildSecret(owner, SmallValueBytes);
        var items = new List<StagedSecret>
        {
            new(oversized, [], [], "etag-oversized"),
            new(healthy, [], [], "etag-healthy"),
        };

        var fence = await store.TakeFenceAsync(owner, transferId, Token);
        var returned = await store.StageSecretsAsync(transferId, items, fence, Token);

        Assert.Equal([oversized.Id], returned);
        Assert.Equal(1, await StagedRowCountAsync(owner, transferId));
        var index = await store.ListIndexAsync(owner, transferId, Token);
        Assert.Single(index);
    }

    [Fact]
    public async Task ReleaseFence_WithCurrentETag_RemovesTheRow()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var fence = await store.TakeFenceAsync(owner, transferId, Token);

        await store.ReleaseFenceAsync(owner, transferId, fence, Token);

        Assert.False(await FenceRowExistsAsync(owner, transferId));
    }

    [Fact]
    public async Task ReleaseFence_AfterAnotherRunnerTookTheFence_LeavesTheirRowAndDoesNotThrow()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var stale = await store.TakeFenceAsync(owner, transferId, Token);
        await store.TakeFenceAsync(owner, transferId, Token);

        await store.ReleaseFenceAsync(owner, transferId, stale, Token);

        Assert.True(await FenceRowExistsAsync(owner, transferId));
    }

    [Fact]
    public async Task ReleaseFence_RowAlreadyGone_DoesNotThrow()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var fence = await store.TakeFenceAsync(owner, transferId, Token);
        await store.ReleaseFenceAsync(owner, transferId, fence, Token);

        var exception = await Record.ExceptionAsync(() => store.ReleaseFenceAsync(owner, transferId, fence, Token));

        Assert.Null(exception);
    }

    [Fact]
    public async Task FenceRow_NotInTheIndex_AndHoldsNoPayload()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var secret = BuildSecret(owner, SmallValueBytes);
        var fence = await store.TakeFenceAsync(owner, transferId, Token);
        await store.StageSecretsAsync(transferId, [new StagedSecret(secret, [], [], "etag-indexed")], fence, Token);

        var index = await store.ListIndexAsync(owner, transferId, Token);
        var row = await VaultsTable.GetEntityIfExistsAsync<TableEntity>(VaultKeys.PartitionKey(owner), VaultKeys.StagingFenceRowKey(transferId), cancellationToken: Token);

        var item = Assert.Single(index);
        Assert.Equal(secret.Id, item.ItemId);
        var fenceRow = row.Value ?? throw new InvalidOperationException("The fence row is missing, so the case proves nothing.");
        Assert.DoesNotContain(fenceRow.Keys, key => key is not ("PartitionKey" or "RowKey" or "Timestamp" or "odata.etag"));
    }

    [Fact]
    public async Task StageSecretsAsync_StaleFence_ThrowsJobChangedAndWritesNothing()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var stale = await StaleFenceAsync(store, owner, transferId);
        var items = new List<StagedSecret> { new(BuildSecret(owner, SmallValueBytes), [], [], "etag-stale") };

        await Assert.ThrowsAsync<TransferJobChangedException>(() => store.StageSecretsAsync(transferId, items, stale, Token));

        Assert.Equal(0, await StagedRowCountAsync(owner, transferId));
        var live = await store.TakeFenceAsync(owner, transferId, Token);
        await store.StageSecretsAsync(transferId, items, live, Token);
        Assert.Equal(1, await StagedRowCountAsync(owner, transferId));
    }

    [Fact]
    public async Task StageFoldersAsync_StaleFence_ThrowsJobChangedAndWritesNothing()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var stale = await StaleFenceAsync(store, owner, transferId);
        var folder = new Folder { Id = Guid.NewGuid(), OwnerId = owner, NameEnvelope = [1, 2, 3], MetadataKeyVersion = 1, CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow };
        var items = new List<StagedFolder> { new(folder, "etag-stale") };

        await Assert.ThrowsAsync<TransferJobChangedException>(() => store.StageFoldersAsync(transferId, items, stale, Token));

        Assert.Empty(await store.ReadFoldersAsync(owner, transferId, Token));
    }

    [Fact]
    public async Task StageSkippedAsync_StaleFence_ThrowsJobChangedAndWritesNothing()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var stale = await StaleFenceAsync(store, owner, transferId);

        await Assert.ThrowsAsync<TransferJobChangedException>(() => store.StageSkippedAsync(owner, transferId, secretId, "etag-stale", stale, Token));

        Assert.Equal(0, await StagedRowCountAsync(owner, transferId));
    }

    [Fact]
    public async Task StageSecretsAsync_MissingFenceRow_ThrowsJobChanged()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var released = await store.TakeFenceAsync(owner, transferId, Token);
        await store.ReleaseFenceAsync(owner, transferId, released, Token);
        var items = new List<StagedSecret> { new(BuildSecret(owner, SmallValueBytes), [], [], "etag-missing") };

        await Assert.ThrowsAsync<TransferJobChangedException>(() => store.StageSecretsAsync(transferId, items, released, Token));

        Assert.Equal(0, await StagedRowCountAsync(owner, transferId));
    }

    [Fact]
    public async Task StageSecretsAsync_ManyItems_ChainsTheFenceAcrossPacks()
    {
        var owner = TestUsers.OtherUser.ObjectId;
        var transferId = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITransferStagingStore>();
        var fence = await store.TakeFenceAsync(owner, transferId, Token);
        var takenETag = fence.ETag;
        var items = Enumerable.Range(0, ManyItemCount)
            .Select(_ => new StagedSecret(BuildSecret(owner, SmallValueBytes), [], [], "etag-many"))
            .ToList();

        var oversized = await store.StageSecretsAsync(transferId, items, fence, Token);

        Assert.Empty(oversized);
        Assert.Equal(ManyItemCount, await StagedRowCountAsync(owner, transferId));
        Assert.NotEqual(takenETag, fence.ETag);
    }

    private static async Task<StageFence> StaleFenceAsync(ITransferStagingStore store, Guid owner, Guid transferId)
    {
        var stale = await store.TakeFenceAsync(owner, transferId, Token);
        await store.TakeFenceAsync(owner, transferId, Token);
        return stale;
    }

    private async Task<bool> FenceRowExistsAsync(Guid owner, Guid transferId) =>
        (await VaultsTable.GetEntityIfExistsAsync<TableEntity>(VaultKeys.PartitionKey(owner), VaultKeys.StagingFenceRowKey(transferId), cancellationToken: Token)).HasValue;

    private static Secret BuildSecret(Guid owner, int valueBytes) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = owner,
        Type = SecretType.Login,
        NameEnvelope = [1, 2, 3],
        ValueEnvelope = new byte[valueBytes],
        DataKey = new WrappedDataKey("staging-test-key", "staging-test-algorithm", [4, 5, 6]),
        MetadataKeyVersion = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        ModifiedAt = DateTimeOffset.UtcNow,
    };
}
