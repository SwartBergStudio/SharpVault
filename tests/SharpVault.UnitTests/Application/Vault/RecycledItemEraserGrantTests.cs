// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RecycledItemEraserGrantTests
{
    private const string ItemETag = "item-etag";
    private const string RecipientETag = "recipient-etag";
    private const int ReadDelayMilliseconds = 5;

    private readonly IVaultStore _store = Substitute.For<IVaultStore>();
    private readonly IShareInboxStore _inbox = Substitute.For<IShareInboxStore>();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly List<IReadOnlyList<VaultChange>> _submitted = [];
    private readonly Dictionary<Guid, List<ShareGrant>> _grantsByItem = [];
    private int _inFlight;
    private int _maxInFlight;

    public RecycledItemEraserGrantTests()
    {
        _store.ListShareGrantsForItemAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Versioned<ShareGrant>>>(
                [.. GrantsOf(call.ArgAt<Guid>(2)).Select(grant => new Versioned<ShareGrant>(grant, ItemETag))]));
        _store.GetShareGrantByRecipientETagAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var current = Interlocked.Increment(ref _inFlight);
                RecordMaxInFlight(current);
                await Task.Delay(ReadDelayMilliseconds, TestContext.Current.CancellationToken);
                Interlocked.Decrement(ref _inFlight);
                return (string?)RecipientETag;
            });
        _store.ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<SecretVersion>>>([]));
        GivenSubmit(changes => new VaultWriteResult(StoreWriteResult.Written, changes.Count, [.. changes.Select(_ => (string?)"w")]));
    }

    private static ShareGrant NewGrant(Guid owner, ShareItemKind kind, Guid itemId) => new()
    {
        OwnerId = owner,
        ItemKind = kind,
        ItemId = itemId,
        RecipientId = Guid.NewGuid(),
        Permission = SharePermission.View,
        RecipientDisplayName = "Share Recipient",
        RecipientEmail = "recipient@example.test",
        GrantedById = owner,
        GrantedByDisplayName = "Share Owner",
        GrantedAt = VaultSamples.Created,
        ModifiedAt = VaultSamples.Created,
    };

    private static Versioned<SecretVersion> VersionOf(Secret secret, string etag) =>
        new(VaultSamples.Version(secret.OwnerId, secret.Id), etag);

    private List<ShareGrant> GrantsOf(Guid itemId) =>
        _grantsByItem.TryGetValue(itemId, out var grants) ? grants : [];

    private void RecordMaxInFlight(int current)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref _maxInFlight);
        }
        while (current > seen && Interlocked.CompareExchange(ref _maxInFlight, current, seen) != seen);
    }

    private List<ShareGrant> Seed(ShareItemKind kind, Guid itemId, int count)
    {
        var grants = Enumerable.Range(0, count).Select(_ => NewGrant(_owner, kind, itemId)).ToList();
        _grantsByItem[itemId] = grants;
        return grants;
    }

    private void GivenSubmit(Func<IReadOnlyList<VaultChange>, VaultWriteResult> script) =>
        _store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                _submitted.Add(changes);
                return Task.FromResult(script(changes));
            });

    private Versioned<Secret> RecycledSecret() => new(VaultSamples.Recycled(_owner), "bin-etag");

    private List<Versioned<Folder>> RecycledFolders(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => new Versioned<Folder>(VaultSamples.RecycledFolder(_owner), "folder-etag"))];

    [Fact]
    public async Task EraseSecret_GrantsFirstVersionsThenSecretLast()
    {
        var binned = RecycledSecret();
        Seed(ShareItemKind.Secret, binned.Value.Id, 2);
        _store.ListVersionsAsync(_owner, binned.Value.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<SecretVersion>>>([VersionOf(binned.Value, "v1"), VersionOf(binned.Value, "v2")]));

        var erased = await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        var changes = Assert.Single(_submitted);
        Assert.Equal(
            [VaultChangeKind.DeleteShareGrant, VaultChangeKind.DeleteShareGrant, VaultChangeKind.DeleteVersion, VaultChangeKind.DeleteVersion, VaultChangeKind.PurgeSecret],
            changes.Select(change => change.Kind));
        Assert.All(changes.Take(2), change =>
        {
            Assert.Equal(ItemETag, change.ETag);
            Assert.Equal(RecipientETag, change.SecondaryETag);
        });
        Assert.Equal("bin-etag", changes[^1].ETag);
        Assert.Equal(2, erased.CommittedGrants.Count);
    }

    [Fact]
    public async Task EraseSecret_NoGrants_OneTransactionAsBefore()
    {
        var binned = RecycledSecret();
        _store.ListVersionsAsync(_owner, binned.Value.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<SecretVersion>>>([VersionOf(binned.Value, "v1")]));

        var erased = await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        Assert.Equal([VaultChangeKind.DeleteVersion, VaultChangeKind.PurgeSecret], Assert.Single(_submitted).Select(change => change.Kind));
        Assert.Empty(erased.Grants);
        await _store.DidNotReceive().GetShareGrantByRecipientETagAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _inbox.DidNotReceive().RemoveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EraseSecret_InboxRemovalFails_ReportsSuccess()
    {
        var binned = RecycledSecret();
        Seed(ShareItemKind.Secret, binned.Value.Id, 3);
        _inbox.RemoveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("inbox down"));

        var erased = await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.Written, erased.Write.Result);
        Assert.Equal(3, erased.CommittedGrants.Count);
        await _inbox.Received(3).RemoveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EraseSecret_PartialWrite_RemovesInboxRowsOnlyOfCommittedGrants()
    {
        var binned = RecycledSecret();
        var grants = Seed(ShareItemKind.Secret, binned.Value.Id, 5);
        GivenSubmit(changes => new VaultWriteResult(StoreWriteResult.Conflict, 2, [.. changes.Select(_ => (string?)null)]));

        var erased = await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.Conflict, erased.Write.Result);
        Assert.Equal(grants.Take(2).Select(grant => grant.RecipientId), erased.CommittedGrants.Select(grant => grant.RecipientId));
        await _inbox.Received(2).RemoveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        foreach (var grant in grants.Take(2))
        {
            await _inbox.Received(1).RemoveAsync(grant.RecipientId, _owner, ShareItemKind.Secret, binned.Value.Id, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task EraseSecret_NothingCommitted_RemovesNoInboxRows()
    {
        var binned = RecycledSecret();
        Seed(ShareItemKind.Secret, binned.Value.Id, 4);
        GivenSubmit(changes => new VaultWriteResult(StoreWriteResult.Conflict, 0, [.. changes.Select(_ => (string?)null)]));

        var erased = await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        Assert.Empty(erased.CommittedGrants);
        await _inbox.DidNotReceive().RemoveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EraseSecret_150Grants_ListsOnceWithDoubleLimitAndReadsOnceMorePerGrant()
    {
        var binned = RecycledSecret();
        Seed(ShareItemKind.Secret, binned.Value.Id, 150);

        var erased = await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        Assert.Equal(150, erased.Grants.Count);
        await _store.Received(1).ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, binned.Value.Id, SharingLimits.MaxGrantsPerItem * 2, Arg.Any<CancellationToken>());
        await _store.Received(150).GetShareGrantByRecipientETagAsync(_owner, ShareItemKind.Secret, binned.Value.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _store.Received(1).ListVersionsAsync(_owner, binned.Value.Id, Arg.Any<CancellationToken>());
        Assert.Equal(150, Assert.Single(_submitted).Count(change => change.Kind == VaultChangeKind.DeleteShareGrant));
    }

    [Fact]
    public async Task EraseSecret_150Grants_RowReadsRunAtMostSixteenAtATime()
    {
        var binned = RecycledSecret();
        Seed(ShareItemKind.Secret, binned.Value.Id, 150);

        await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        Assert.InRange(_maxInFlight, 2, SharingLimits.ItemReadParallelism);
    }

    [Fact]
    public async Task EraseSecret_Grants_RemoveInboxRowForEveryRecipientOnce()
    {
        var binned = RecycledSecret();
        var grants = Seed(ShareItemKind.Secret, binned.Value.Id, 150);

        await EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, TestContext.Current.CancellationToken);

        foreach (var grant in grants)
        {
            await _inbox.Received(1).RemoveAsync(grant.RecipientId, _owner, ShareItemKind.Secret, binned.Value.Id, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task EraseSecret_Cancelled_SubmitsNothing()
    {
        var binned = RecycledSecret();
        Seed(ShareItemKind.Secret, binned.Value.Id, 3);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EraserFactory.For(_store, _inbox).EraseSecretAsync(_owner, binned, cancelled.Token));

        Assert.Empty(_submitted);
    }

    [Fact]
    public async Task EraseFolders_ListsGrantsPerFolder()
    {
        var folders = RecycledFolders(3);
        foreach (var folder in folders)
        {
            Seed(ShareItemKind.Folder, folder.Value.Id, 2);
        }

        var erased = await EraserFactory.For(_store, _inbox).EraseFoldersAsync(_owner, folders, TestContext.Current.CancellationToken);

        foreach (var folder in folders)
        {
            await _store.Received(1).ListShareGrantsForItemAsync(_owner, ShareItemKind.Folder, folder.Value.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        var changes = Assert.Single(_submitted);
        Assert.Equal(6, erased.Grants.Count);
        Assert.Equal(
            [.. Enumerable.Repeat(VaultChangeKind.DeleteShareGrant, 6), .. Enumerable.Repeat(VaultChangeKind.PurgeFolder, 3)],
            changes.Select(change => change.Kind));
        Assert.Equal(folders.Select(folder => folder.Value.Id), changes.Skip(6).Select(change => change.ItemId));
    }

    [Fact]
    public async Task EraseFolders_NoGrants_SendsOnlyThePurgesInOrder()
    {
        var folders = RecycledFolders(3);

        var erased = await EraserFactory.For(_store, _inbox).EraseFoldersAsync(_owner, folders, TestContext.Current.CancellationToken);

        Assert.Empty(erased.Grants);
        Assert.Equal(
            folders.Select(folder => folder.Value.Id),
            Assert.Single(_submitted).Select(change => change.ItemId));
    }

    [Fact]
    public async Task EraseFolders_ManyFoldersWithGrants_ReadsStayWithinBothParallelismLimits()
    {
        var folders = RecycledFolders(8);
        foreach (var folder in folders)
        {
            Seed(ShareItemKind.Folder, folder.Value.Id, 40);
        }

        var erased = await EraserFactory.For(_store, _inbox).EraseFoldersAsync(_owner, folders, TestContext.Current.CancellationToken);

        Assert.Equal(320, erased.Grants.Count);
        await _store.Received(320).GetShareGrantByRecipientETagAsync(_owner, ShareItemKind.Folder, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.InRange(_maxInFlight, 2, VaultLimits.FolderQueryParallelism * SharingLimits.ItemReadParallelism);
    }
}
