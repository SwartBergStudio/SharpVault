// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class PurgeGrantCascadeStoreTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const int GrantCount = 150;
    private const int VersionCount = 2;
    private const int MiddleChunkGrantIndex = 74;
    private const string StaleETag = "W/\"datetime'2000-01-01T00%3A00%3A00.0000000Z'\"";

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

    private static Versioned<SharpVault.Domain.Vaults.Secret> WithStaleETag(Versioned<SharpVault.Domain.Vaults.Secret> binned) =>
        new(binned.Value, StaleETag);

    private static IVaultStore StoreFailingTheMiddleChunk(IVaultStore real)
    {
        var store = Substitute.For<IVaultStore>();
        var staleRecipient = Guid.Empty;
        store.ListShareGrantsForItemAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var listed = await real.ListShareGrantsForItemAsync(call.ArgAt<Guid>(0), call.ArgAt<ShareItemKind>(1), call.ArgAt<Guid>(2), call.ArgAt<int>(3), call.ArgAt<CancellationToken>(4));
                staleRecipient = listed[MiddleChunkGrantIndex].Value.RecipientId;
                return listed;
            });
        store.GetShareGrantByRecipientETagAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var etag = await real.GetShareGrantByRecipientETagAsync(call.ArgAt<Guid>(0), call.ArgAt<ShareItemKind>(1), call.ArgAt<Guid>(2), call.ArgAt<Guid>(3), call.ArgAt<CancellationToken>(4));
                return etag is not null && call.ArgAt<Guid>(3) == staleRecipient ? StaleETag : etag;
            });
        store.ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => real.ListVersionsAsync(call.ArgAt<Guid>(0), call.ArgAt<Guid>(1), call.ArgAt<CancellationToken>(2)));
        store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call => real.SubmitAsync(call.ArgAt<Guid>(0), call.ArgAt<IReadOnlyList<VaultChange>>(1), call.ArgAt<CancellationToken>(2)));
        return store;
    }

    [Fact]
    public async Task EraseSecret_150GrantsSplitAcrossChunks_LeavesNoGrantInboxVersionOrSecretRows()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);
        Assert.Equal(GrantCount, await world.InboxRowCountAsync(Token));

        var erased = await world.Eraser.EraseSecretAsync(_owner, binned, Token);

        Assert.Equal(StoreWriteResult.Written, erased.Write.Result);
        Assert.Equal(GrantCount, erased.CommittedGrants.Count);
        Assert.Equal(grants.Select(grant => grant.RecipientId).Order(), erased.CommittedGrants.Select(grant => grant.RecipientId).Order());
        Assert.Empty(await world.PartitionRowsAsync(Token));
        Assert.Equal(0, await world.InboxRowCountAsync(Token));
        Assert.Null(await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token));
    }

    [Fact]
    public async Task EraseSecret_LastChunkConflicts_AllGrantsGoneSecretAndVersionsRemainAndRepeatFinishes()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);

        var interrupted = await world.Eraser.EraseSecretAsync(_owner, WithStaleETag(binned), Token);

        Assert.Equal(StoreWriteResult.Conflict, interrupted.Write.Result);
        Assert.Equal(GrantCount, interrupted.CommittedGrants.Count);
        Assert.Equal(0, await world.GrantsStillStoredAsync(grants, Token));
        Assert.Equal(0, await world.InboxRowCountAsync(Token));
        Assert.NotNull(await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token));
        Assert.Equal(VersionCount, (await world.Store.ListVersionsAsync(_owner, binned.Value.Id, Token)).Count);

        var fresh = (await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token))!;
        var repeat = await world.Eraser.EraseSecretAsync(_owner, fresh, Token);

        Assert.Equal(StoreWriteResult.Written, repeat.Write.Result);
        Assert.Empty(repeat.Grants);
        Assert.Empty(await world.PartitionRowsAsync(Token));
        Assert.Equal(0, await world.InboxRowCountAsync(Token));
    }

    [Fact]
    public async Task EraseSecret_MiddleChunkConflicts_CommittedGrantsMatchTheStoredStateAndRepeatFinishes()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);

        var interrupted = await world.EraserOver(StoreFailingTheMiddleChunk(world.Store)).EraseSecretAsync(_owner, binned, Token);

        var remaining = await world.GrantsStillStoredAsync(grants, Token);
        Assert.Equal(StoreWriteResult.Conflict, interrupted.Write.Result);
        Assert.InRange(interrupted.CommittedGrants.Count, 1, GrantCount - 1);
        Assert.Equal(GrantCount - interrupted.CommittedGrants.Count, remaining);
        Assert.Equal(remaining, await world.InboxRowCountAsync(Token));
        foreach (var committed in interrupted.CommittedGrants)
        {
            Assert.Null(await world.Store.GetShareGrantRowsAsync(_owner, committed.ItemKind, committed.ItemId, committed.RecipientId, Token));
        }

        var fresh = (await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token))!;
        var repeat = await world.Eraser.EraseSecretAsync(_owner, fresh, Token);

        Assert.Equal(StoreWriteResult.Written, repeat.Write.Result);
        Assert.Equal(remaining, repeat.CommittedGrants.Count);
        Assert.Empty(await world.PartitionRowsAsync(Token));
        Assert.Equal(0, await world.InboxRowCountAsync(Token));
    }

    [Fact]
    public async Task InterruptedPurge_GrantsGoneSecretStillBinned_RecipientsReachNothingAndOwnerRestoreHasNoShares()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);
        await world.Eraser.EraseSecretAsync(_owner, WithStaleETag(binned), Token);

        Assert.Null(await world.Store.GetSecretAsync(_owner, binned.Value.Id, Token));
        foreach (var grant in grants)
        {
            Assert.Null(await world.Store.GetShareGrantAsync(_owner, ShareItemKind.Secret, binned.Value.Id, grant.RecipientId, Token));
            Assert.Empty(await world.Store.ListFolderGrantsToRecipientAsync(_owner, grant.RecipientId, Token));
        }

        await world.RestoreAsync(binned.Value.Id, Token);

        Assert.NotNull(await world.Store.GetSecretAsync(_owner, binned.Value.Id, Token));
        Assert.Empty(await world.Store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, binned.Value.Id, GrantCount * 2, Token));
        Assert.Equal(VersionCount, (await world.Store.ListVersionsAsync(_owner, binned.Value.Id, Token)).Count);
    }

    [Fact]
    public async Task InterruptedPurge_MiddleChunk_RestoreRevivesOnlyTheGrantsNotYetErased()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);
        var interrupted = await world.EraserOver(StoreFailingTheMiddleChunk(world.Store)).EraseSecretAsync(_owner, binned, Token);
        var erasedIds = interrupted.CommittedGrants.Select(grant => grant.RecipientId).ToHashSet();

        Assert.Null(await world.Store.GetSecretAsync(_owner, binned.Value.Id, Token));
        await world.RestoreAsync(binned.Value.Id, Token);

        var revived = await world.Store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, binned.Value.Id, GrantCount * 2, Token);
        Assert.Equal(GrantCount - erasedIds.Count, revived.Count);
        Assert.DoesNotContain(revived, grant => erasedIds.Contains(grant.Value.RecipientId));
        foreach (var grant in grants.Where(grant => erasedIds.Contains(grant.RecipientId)))
        {
            Assert.Null(await world.Store.GetShareGrantAsync(_owner, ShareItemKind.Secret, binned.Value.Id, grant.RecipientId, Token));
        }
    }

    [Fact]
    public async Task EraseSecret_RowPairDamaged_StillErasesTheOthersAndTheSecret()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(3, VersionCount, Token);
        await DeleteRowAsync(VaultKeys.GrantByRecipientRowKey(grants[1].RecipientId, ShareItemKind.Secret, binned.Value.Id));

        var erased = await world.Eraser.EraseSecretAsync(_owner, binned, Token);

        Assert.Equal(StoreWriteResult.Written, erased.Write.Result);
        Assert.Equal(3, erased.CommittedGrants.Count);
        Assert.Contains(erased.CommittedGrants, grant => grant.RecipientId == grants[1].RecipientId);
        Assert.Null(await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token));
    }

    [Fact]
    public async Task EraseSecret_RowPairDamaged_ThePurgedItemGrantsNoAccess()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(3, 0, Token);
        await DeleteRowAsync(VaultKeys.GrantByRecipientRowKey(grants[1].RecipientId, ShareItemKind.Secret, binned.Value.Id));

        await world.Eraser.EraseSecretAsync(_owner, binned, Token);

        Assert.Null(await world.Store.GetSecretAsync(_owner, binned.Value.Id, Token));
        Assert.Null(await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token));
        Assert.Null(await world.Store.GetShareGrantRowsAsync(_owner, ShareItemKind.Secret, binned.Value.Id, grants[1].RecipientId, Token));
    }

    [Fact]
    public async Task EraseSecret_RowPairDamagedThenInterrupted_RepeatStillFinishes()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(GrantCount, VersionCount, Token);
        await DeleteRowAsync(VaultKeys.GrantByRecipientRowKey(grants[7].RecipientId, ShareItemKind.Secret, binned.Value.Id));
        await world.Eraser.EraseSecretAsync(_owner, WithStaleETag(binned), Token);

        var fresh = (await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token))!;
        var repeat = await world.Eraser.EraseSecretAsync(_owner, fresh, Token);

        Assert.Equal(StoreWriteResult.Written, repeat.Write.Result);
        Assert.Null(await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token));
    }

    [Fact]
    public async Task EraseSecret_ByRecipientRowMissing_NoGrantRowOfThePurgedItemRemains()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(3, VersionCount, Token);
        await DeleteRowAsync(VaultKeys.GrantByRecipientRowKey(grants[1].RecipientId, ShareItemKind.Secret, binned.Value.Id));

        await world.Eraser.EraseSecretAsync(_owner, binned, Token);

        Assert.Empty(await world.PartitionRowsAsync(Token));
    }

    [Fact]
    public async Task EraseSecret_ByItemRowMissing_ByRecipientRowSurvivesGrantsNothingAndDoesNotBlockThePurge()
    {
        var world = World;
        var (binned, grants) = await world.SeedRecycledSecretAsync(3, VersionCount, Token);
        await DeleteRowAsync(VaultKeys.GrantByItemRowKey(ShareItemKind.Secret, binned.Value.Id, grants[1].RecipientId));

        var erased = await world.Eraser.EraseSecretAsync(_owner, binned, Token);

        Assert.Equal(StoreWriteResult.Written, erased.Write.Result);
        Assert.Equal(2, erased.CommittedGrants.Count);
        Assert.Null(await world.Store.GetSecretAsync(_owner, binned.Value.Id, Token));
        Assert.Null(await world.Store.GetRecycledSecretAsync(_owner, binned.Value.Id, Token));
        var survivor = Assert.Single(await world.PartitionRowsAsync(Token));
        Assert.Equal(VaultKeys.GrantByRecipientRowKey(grants[1].RecipientId, ShareItemKind.Secret, binned.Value.Id), survivor.RowKey);
        Assert.Null(await world.Store.GetShareGrantRowsAsync(_owner, ShareItemKind.Secret, binned.Value.Id, grants[1].RecipientId, Token));
        Assert.Empty((await world.Store.ListShareGrantsByOwnerAsync(_owner, 100, null, Token)).Items);
    }

    private async Task DeleteRowAsync(string rowKey) =>
        await GetTableClient(TableNames.Vaults).DeleteEntityAsync(VaultKeys.PartitionKey(_owner), rowKey, cancellationToken: Token);
}
