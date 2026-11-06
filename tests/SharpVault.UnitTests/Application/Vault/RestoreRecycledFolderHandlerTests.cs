// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RestoreRecycledFolderHandlerTests : VaultHandlerTestBase
{
    private RestoreRecycledFolderCommandHandler Handler() =>
        new(Store, KeyRings, Audit, Current, Time, NullLogger<RestoreRecycledFolderCommandHandler>.Instance);

    private Folder[] ActiveChain(int length)
    {
        var folders = new List<Folder>();
        Guid? parent = null;
        for (var level = 0; level < length; level++)
        {
            var folder = NewFolder(parent);
            folders.Add(folder);
            parent = folder.Id;
        }

        return [.. folders];
    }

    [Fact]
    public async Task Handle_BatchRoot_RestoresWholeBatch()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        var inRoot = RecycledSecretIn(batch, root.Id);
        var inChild = RecycledSecretIn(batch, child.Id);
        var recycled = GivenBatch(batch, [root, child], [inRoot, inChild]);
        inRoot.Revision = 3;

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [VaultChangeKind.TouchKeyRing, VaultChangeKind.RestoreSecret, VaultChangeKind.RestoreSecret, VaultChangeKind.RestoreFolder, VaultChangeKind.RestoreFolder],
            LastSubmit.Select(change => change.Kind));
        Assert.Equal(RingETag, LastSubmit[0].ETag);
        Assert.All(LastSubmit.Skip(1), change => Assert.Null(change.Secret is { } secret ? secret.DeletedAt : change.Folder!.DeletedAt));
        Assert.All(LastSubmit.Skip(1), change => Assert.Null(change.Secret is { } secret ? secret.DeletionBatchId : change.Folder!.DeletionBatchId));
        Assert.Equal(root.Id, result.Value!.Id);
        Assert.Equal(4, inRoot.Revision);
        Assert.Equal(recycled.Folders[0].ETag, LastSubmit[4].ETag);
        Assert.Equal(root.Id, LastSubmit[4].Folder!.Id);
        Assert.Equal(child.Id, LastSubmit[3].Folder!.Id);
        Assert.Equal(root.Id, child.ParentId);
    }

    [Fact]
    public async Task Handle_Subfolder_RestoresOnlyItsSubtreeToRoot()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var middle = RecycledFolderIn(batch, root.Id);
        var leaf = RecycledFolderIn(batch, middle.Id);
        var inRoot = RecycledSecretIn(batch, root.Id);
        var inMiddle = RecycledSecretIn(batch, middle.Id);
        var inLeaf = RecycledSecretIn(batch, leaf.Id);
        GivenBatch(batch, [root, middle, leaf], [inRoot, inMiddle, inLeaf]);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(middle.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var restoredFolders = LastSubmit.Where(change => change.Kind == VaultChangeKind.RestoreFolder).Select(change => change.Folder!.Id).ToList();
        var restoredSecrets = LastSubmit.Where(change => change.Kind == VaultChangeKind.RestoreSecret).Select(change => change.Secret!.Id).Order().ToList();
        Assert.Equal([leaf.Id, middle.Id], restoredFolders);
        Assert.Equal(new[] { inMiddle.Id, inLeaf.Id }.Order(), restoredSecrets);
        Assert.Null(middle.ParentId);
        Assert.Equal(middle.Id, leaf.ParentId);
        Assert.NotNull(root.DeletedAt);
        Assert.NotNull(inRoot.DeletedAt);
    }

    [Fact]
    public async Task Handle_ParentActive_RestoresUnderParent()
    {
        var parent = NewFolder();
        GivenFolders(parent);
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch, parent.Id);
        GivenBatch(batch, [root], []);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(parent.Id, root.ParentId);
        Assert.Equal(parent.Id, result.Value!.ParentId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ParentGone_RestoresToRoot()
    {
        GivenFolders(NewFolder());
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch, Guid.NewGuid());
        GivenBatch(batch, [root], []);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(root.ParentId);
        Assert.Null(result.Value!.ParentId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OriginalParentNowTooDeep_RestoresToRoot()
    {
        var chain = ActiveChain(VaultLimits.MaxFolderDepth);
        GivenFolders(chain);
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch, chain[^1].Id);
        GivenBatch(batch, [root], []);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(root.ParentId);
    }

    [Fact]
    public async Task Handle_SubtreeHeightPlusParentDepthExceedsMax_RestoresToRoot()
    {
        var chain = ActiveChain(8);
        GivenFolders(chain);
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch, chain[^1].Id);
        var child = RecycledFolderIn(batch, root.Id);
        var grandchild = RecycledFolderIn(batch, child.Id);
        GivenBatch(batch, [root, child, grandchild], []);

        await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Null(root.ParentId);
    }

    [Fact]
    public async Task Handle_SubtreeFitsExactlyUnderParent_RestoresUnderParent()
    {
        var chain = ActiveChain(7);
        GivenFolders(chain);
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch, chain[^1].Id);
        var child = RecycledFolderIn(batch, root.Id);
        var grandchild = RecycledFolderIn(batch, child.Id);
        GivenBatch(batch, [root, child, grandchild], []);

        await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(chain[^1].Id, root.ParentId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderLimitWouldBeExceeded_ReturnsRejected()
    {
        GivenFolders([.. Enumerable.Range(0, VaultLimits.MaxFoldersPerVault - 1).Select(_ => VaultSamples.Folder(Owner))]);
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        GivenBatch(batch, [root, child], []);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderLimitReached, result.Error);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_FolderLimitExactlyReached_Succeeds()
    {
        GivenFolders([.. Enumerable.Range(0, VaultLimits.MaxFoldersPerVault - 2).Select(_ => VaultSamples.Folder(Owner))]);
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        GivenBatch(batch, [root, child], []);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_MoreThan1000Items_ReturnsRejectedWithoutSubmit()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var secrets = Enumerable.Range(0, VaultLimits.MaxItemsPerFolderOperation).Select(_ => RecycledSecretIn(batch, root.Id)).ToList();
        GivenBatch(batch, [root], secrets);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderOperationTooLarge, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_NotInBin_ReturnsNotFound()
    {
        var result = await Handler().Handle(new RestoreRecycledFolderCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ActiveFolderId_ReturnsNotFound()
    {
        var active = GivenFolder();

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(active.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderInAnotherOwnersBin_ReturnsNotFound()
    {
        var foreign = RecycledFolderIn(Guid.NewGuid());
        Store.GetRecycledFolderAsync(Guid.NewGuid(), foreign.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(new Versioned<Folder>(foreign, ETagOf(1))));

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(foreign.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await Store.Received().GetRecycledFolderAsync(Owner, foreign.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        GivenBatch(batch, [root], []);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_StoredCycleInBatch_ReturnsRejectedTreeInvalid()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var a = RecycledFolderIn(batch);
        var b = RecycledFolderIn(batch, a.Id);
        a.ParentId = b.Id;
        GivenBatch(batch, [a, b], []);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(a.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderTreeInvalid, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_BatchMissingTheRequestedFolder_ReturnsRejectedTreeInvalid()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        GivenBatch(batch, [root], []);
        Store.ListRecycledBatchAsync(Owner, batch, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new RecycledBatch([], [])));

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_AuditsEveryRestoredItem()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        var secret = RecycledSecretIn(batch, child.Id);
        GivenBatch(batch, [root, child], [secret]);

        await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(3, AuditLog.Entries.Count);
        Assert.Equal(
            new[] { root.Id, child.Id }.Select(id => id.ToString("D")).Order(),
            AuditLog.Entries.Where(entry => entry.EventType == AuditEventType.FolderRestored).Select(entry => entry.TargetId!).Order());
        var restoredSecret = Assert.Single(AuditLog.Entries, entry => entry.EventType == AuditEventType.SecretRestored);
        Assert.Equal(secret.Id, restoredSecret.SecretId);
        Assert.Equal(secret.Id.ToString("D"), restoredSecret.TargetId);
    }

    [Fact]
    public async Task Handle_ResultCarriesTheRootFolderAndItsNewETag()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        GivenBatch(batch, [root, child], []);
        NextWrite = WrittenWith(ETagOf(70), ETagOf(71), ETagOf(72));

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(root.Id, result.Value!.Id);
        Assert.False(result.Value.IsDamaged);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        GivenBatch(batch, [root], []);
        NextWrite = Failed(write);

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task RestoreFolder_SecondChunkConflicts_AuditsCommittedItems()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        var secrets = Enumerable.Range(0, 60).Select(_ => RecycledSecretIn(batch, child.Id)).ToList();
        GivenBatch(batch, [root, child], secrets);
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 41, changes));

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(40, AuditLog.Entries.Count);
        Assert.Equal(
            LastSubmit.Skip(1).Take(40).Select(change => change.ItemId.ToString("D")).Order(),
            AuditLog.Entries.Select(entry => entry.TargetId!).Order());
    }

    [Fact]
    public async Task RestoreFolder_FirstChunkConflicts_AuditsNothing()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        GivenBatch(batch, [root], [RecycledSecretIn(batch, root.Id)]);
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 0, changes));

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    private void GivenRecycledTree(Guid batch, Guid rootId, Guid childId, IReadOnlyList<Guid> secretIds, HashSet<Guid> alreadyRestored)
    {
        var folders = new List<Folder>();
        if (!alreadyRestored.Contains(rootId))
        {
            var root = RecycledFolderIn(batch);
            root.Id = rootId;
            folders.Add(root);
        }

        if (!alreadyRestored.Contains(childId))
        {
            var child = RecycledFolderIn(batch, rootId);
            child.Id = childId;
            folders.Add(child);
        }

        var secrets = secretIds.Where(id => !alreadyRestored.Contains(id)).Select(id =>
        {
            var secret = RecycledSecretIn(batch, childId);
            secret.Id = id;
            return secret;
        }).ToList();

        GivenBatch(batch, folders, secrets);
        foreach (var id in alreadyRestored)
        {
            Store.GetRecycledFolderAsync(Owner, id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(null));
        }
    }

    [Fact]
    public async Task RestoreFolder_SecondChunkConflicts_RepeatFinishes()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var (rootId, childId) = (Guid.NewGuid(), Guid.NewGuid());
        var secretIds = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToList();
        GivenRecycledTree(batch, rootId, childId, secretIds, new HashSet<Guid>());
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 41, changes));

        var first = await Handler().Handle(new RestoreRecycledFolderCommand(rootId), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, first.FailureKind);
        var committed = Submitted[0].Skip(1).Take(40).Select(change => change.ItemId).ToHashSet();
        GivenRecycledTree(batch, rootId, childId, secretIds, committed);
        GivenSubmitScript(_ => null);

        var second = await Handler().Handle(new RestoreRecycledFolderCommand(rootId), TestContext.Current.CancellationToken);

        Assert.True(second.IsSuccess, second.Error);
        var repeated = Submitted[1].Skip(1).Select(change => change.ItemId);
        Assert.Equal(secretIds.Append(rootId).Append(childId).Order(), committed.Concat(repeated).Order());
        Assert.Equal(rootId, second.Value!.Id);
    }

    [Fact]
    public async Task Handle_Always_PutsTheRootFolderLast()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var middle = RecycledFolderIn(batch, root.Id);
        var leaf = RecycledFolderIn(batch, middle.Id);
        var recycled = GivenBatch(batch, [root, middle, leaf], [RecycledSecretIn(batch, leaf.Id)]);
        NextWrite = WrittenWith(ETagOf(70), ETagOf(71), ETagOf(72), ETagOf(73), ETagOf(74));

        var result = await Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.TouchKeyRing, VaultChangeKind.RestoreSecret, VaultChangeKind.RestoreFolder, VaultChangeKind.RestoreFolder, VaultChangeKind.RestoreFolder], LastSubmit.Select(change => change.Kind));
        Assert.Equal([leaf.Id, middle.Id, root.Id], LastSubmit.Skip(2).Select(change => change.Folder!.Id));
        Assert.Equal(root.Id, result.Value!.Id);
        Assert.Equal(recycled.Folders[0].Value.Id, result.Value.Id);
    }

    [Fact]
    public async Task RestoreFolder_RingReadAfterConcurrentCommit_CannotUseStaleList()
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        GivenBatch(batch, [root], []);
        var staleActive = Enumerable.Range(0, VaultLimits.MaxFoldersPerVault - 1).Select(_ => new Versioned<Folder>(VaultSamples.Folder(Owner), ETagOf(1))).ToList();
        IReadOnlyList<Versioned<Folder>> current = staleActive;
        var ringRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ringReleased = new TaskCompletionSource<Versioned<VaultKeyRing>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Store.GetKeyRingAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            ringRequested.TrySetResult();
            return ringReleased.Task;
        });
        Store.ListFoldersAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(current));

        var handling = Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);
        await ringRequested.Task;
        current = [.. staleActive, new Versioned<Folder>(VaultSamples.Folder(Owner), ETagOf(2))];
        ringReleased.SetResult(new Versioned<VaultKeyRing>(Ring, ETagOf(9)));
        var result = await handling;

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderLimitReached, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task RestoreFolder_SecondChunkStorageFails_AuditsCommittedItems()
    {
        GivenFolders();
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        var secrets = Enumerable.Range(0, 60).Select(_ => RecycledSecretIn(batch, child.Id)).ToList();
        GivenBatch(batch, [root, child], secrets);
        var failure = new TimeoutException("storage down");
        GivenSubmitScript(changes => Interrupted(failure, 41, changes));

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new RestoreRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(40, AuditLog.Entries.Count);
    }
}
