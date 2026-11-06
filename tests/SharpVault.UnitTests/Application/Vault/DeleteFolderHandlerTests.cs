// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Folders.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class DeleteFolderHandlerTests : VaultHandlerTestBase
{
    private DeleteFolderCommandHandler Handler() => new(Store, KeyRings, Audit, Current, Time, NullLogger<DeleteFolderCommandHandler>.Instance);

    private List<Versioned<Secret>> GivenSecretsIn(Guid folderId, int count)
    {
        var rows = Enumerable.Range(0, count)
            .Select(_ => new Versioned<Secret>(VaultSamples.Secret(Owner, folderId: folderId), NextETag()))
            .ToList();
        Store.ListSecretsInFolderAsync(Owner, folderId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<Secret>>>(rows));
        return rows;
    }

    private static DeleteFolderCommand Delete(Folder folder, FolderDeleteModeDto mode) => new(folder.Id, mode);

    [Fact]
    public async Task Handle_MoveToParent_MovesSecretsAndChildrenUpAndRecyclesFolder()
    {
        var parent = NewFolder();
        var target = NewFolder(parent.Id);
        var childOne = NewFolder(target.Id);
        var childTwo = NewFolder(target.Id);
        var grandchild = NewFolder(childOne.Id);
        var listed = GivenFolders(parent, target, childOne, childTwo, grandchild);
        var secrets = GivenSecretsIn(target.Id, 2);
        var revisions = secrets.Select(secret => secret.Value.Revision).ToList();

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [VaultChangeKind.TouchKeyRing, VaultChangeKind.ReplaceSecret, VaultChangeKind.ReplaceSecret, VaultChangeKind.ReplaceFolder, VaultChangeKind.ReplaceFolder, VaultChangeKind.RecycleFolder],
            LastSubmit.Select(change => change.Kind));
        Assert.Equal(RingETag, LastSubmit[0].ETag);
        Assert.All(secrets, secret => Assert.Equal(parent.Id, secret.Value.FolderId));
        Assert.Equal(revisions.Select(revision => revision + 1), secrets.Select(secret => secret.Value.Revision));
        Assert.All(secrets, secret => Assert.Equal(Now, secret.Value.ModifiedAt));
        Assert.Equal(parent.Id, childOne.ParentId);
        Assert.Equal(parent.Id, childTwo.ParentId);
        Assert.Equal(childOne.Id, grandchild.ParentId);
        var recycled = LastSubmit[^1];
        Assert.Equal(target.Id, recycled.ItemId);
        Assert.Equal(listed[1].ETag, recycled.ETag);
        Assert.Equal(Now, recycled.Folder!.DeletedAt);
        Assert.NotNull(recycled.Folder.DeletionBatchId);
    }

    [Fact]
    public async Task Handle_MoveToParentAtRoot_MovesContentsToRoot()
    {
        var target = NewFolder();
        var child = NewFolder(target.Id);
        GivenFolders(target, child);
        var secrets = GivenSecretsIn(target.Id, 1);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(secrets[0].Value.FolderId);
        Assert.Null(child.ParentId);
    }

    [Fact]
    public async Task Handle_MoveToParentWhenParentIsMissing_MovesContentsToRoot()
    {
        var target = NewFolder(Guid.NewGuid());
        GivenFolders(target);
        var secrets = GivenSecretsIn(target.Id, 1);

        await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.Null(secrets[0].Value.FolderId);
    }

    [Fact]
    public async Task Handle_MoveToParentEmptyFolder_OnlyRecyclesIt()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 0);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.TouchKeyRing, VaultChangeKind.RecycleFolder], LastSubmit.Select(change => change.Kind));
    }

    [Fact]
    public async Task Handle_DeleteContents_RecyclesSubtreeWithOneBatchIdSecretsFirstDeepestFoldersNextRootLast()
    {
        var other = NewFolder();
        var target = NewFolder();
        var child = NewFolder(target.Id);
        var grandchild = NewFolder(child.Id);
        GivenFolders(other, target, child, grandchild);
        GivenSecretsIn(target.Id, 1);
        GivenSecretsIn(child.Id, 2);
        GivenSecretsIn(grandchild.Id, 1);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var kinds = LastSubmit.Select(change => change.Kind).ToList();
        Assert.Equal(VaultChangeKind.TouchKeyRing, kinds[0]);
        Assert.Equal(4, kinds.Count(kind => kind == VaultChangeKind.RecycleSecret));
        Assert.Equal(3, kinds.Count(kind => kind == VaultChangeKind.RecycleFolder));
        Assert.Equal(kinds.Where(kind => kind == VaultChangeKind.RecycleSecret).Count(), kinds.Skip(1).TakeWhile(kind => kind == VaultChangeKind.RecycleSecret).Count());
        var folderOrder = LastSubmit.Where(change => change.Kind == VaultChangeKind.RecycleFolder).Select(change => change.ItemId).ToList();
        Assert.Equal([grandchild.Id, child.Id, target.Id], folderOrder);
        var batches = LastSubmit.Skip(1).Select(change => change.Secret?.DeletionBatchId ?? change.Folder!.DeletionBatchId).Distinct().ToList();
        Assert.Single(batches);
        Assert.NotEqual(Guid.Empty, batches[0]);
        Assert.All(LastSubmit.Skip(1), change => Assert.Equal(Now, change.Secret?.DeletedAt ?? change.Folder!.DeletedAt));
        Assert.Null(other.DeletedAt);
    }

    [Fact]
    public async Task Handle_DeleteContents_SiblingsAreRecycledBeforeTheirParent()
    {
        var target = NewFolder();
        var first = NewFolder(target.Id);
        var second = NewFolder(target.Id);
        GivenFolders(target, first, second);
        GivenSecretsIn(target.Id, 0);
        GivenSecretsIn(first.Id, 0);
        GivenSecretsIn(second.Id, 0);

        await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        var folderOrder = LastSubmit.Where(change => change.Kind == VaultChangeKind.RecycleFolder).Select(change => change.ItemId).ToList();
        Assert.Equal(target.Id, folderOrder[^1]);
        Assert.Equal(3, folderOrder.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_MoveToParentOver1000Items_ReturnsRejectedWithoutSubmit()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, VaultLimits.MaxItemsPerFolderOperation);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderOperationTooLarge, result.Error);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Over1000Items_ReturnsRejectedWithoutSubmit()
    {
        var target = NewFolder();
        var child = NewFolder(target.Id);
        GivenFolders(target, child);
        GivenSecretsIn(target.Id, 600);
        GivenSecretsIn(child.Id, 400);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderOperationTooLarge, result.Error);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_ExactlyAtTheItemLimit_Succeeds()
    {
        var target = NewFolder();
        var child = NewFolder(target.Id);
        GivenFolders(target, child);
        GivenSecretsIn(target.Id, 600);
        GivenSecretsIn(child.Id, 398);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1 + 998 + 2, LastSubmit.Count);
    }

    [Fact]
    public async Task Handle_MoveToParentExactlyAtTheItemLimit_Succeeds()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, VaultLimits.MaxItemsPerFolderOperation - 1);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_FolderMissing_ReturnsNotFound()
    {
        GivenFolders(NewFolder());

        var result = await Handler().Handle(new DeleteFolderCommand(Guid.NewGuid(), FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderOfAnotherOwner_ReturnsNotFoundAndReadsNoSecrets()
    {
        GivenFolders(NewFolder());
        var foreign = GivenFolder(partition: Guid.NewGuid());

        var result = await Handler().Handle(new DeleteFolderCommand(foreign.Value.Id, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        await Store.DidNotReceive().ListSecretsInFolderAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        var target = NewFolder();
        GivenFolders(target);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_StoredCycleInSubtree_ReturnsRejectedTreeInvalid()
    {
        var a = NewFolder();
        var b = NewFolder(a.Id);
        a.ParentId = b.Id;
        GivenFolders(a, b);

        var result = await Handler().Handle(Delete(a, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderTreeInvalid, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_AuditsEveryItem()
    {
        var target = NewFolder();
        var child = NewFolder(target.Id);
        GivenFolders(target, child);
        var inTarget = GivenSecretsIn(target.Id, 2);
        var inChild = GivenSecretsIn(child.Id, 1);

        await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(5, AuditLog.Entries.Count);
        var deletedSecrets = AuditLog.Entries.Where(entry => entry.EventType == AuditEventType.SecretDeleted).ToList();
        Assert.Equal(
            inTarget.Concat(inChild).Select(secret => secret.Value.Id).Order(),
            deletedSecrets.Select(entry => entry.SecretId!.Value).Order());
        Assert.All(deletedSecrets, entry => Assert.Equal(entry.SecretId!.Value.ToString("D"), entry.TargetId));
        var deletedFolders = AuditLog.Entries.Where(entry => entry.EventType == AuditEventType.FolderDeleted).ToList();
        Assert.Equal(new[] { target.Id, child.Id }.Select(id => id.ToString("D")).Order(), deletedFolders.Select(entry => entry.TargetId!).Order());
        Assert.All(deletedFolders, entry => Assert.Null(entry.SecretId));
        Assert.All(AuditLog.Entries, entry => Assert.Equal(Owner, entry.OwnerId));
    }

    [Fact]
    public async Task Handle_MoveToParent_AuditsMovesAndTheDeletion()
    {
        var target = NewFolder();
        var child = NewFolder(target.Id);
        GivenFolders(target, child);
        GivenSecretsIn(target.Id, 2);

        await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.Equal(2, AuditLog.Entries.Count(entry => entry.EventType == AuditEventType.SecretMoved));
        Assert.Equal(1, AuditLog.Entries.Count(entry => entry.EventType == AuditEventType.FolderMoved));
        Assert.Equal(1, AuditLog.Entries.Count(entry => entry.EventType == AuditEventType.FolderDeleted));
    }

    [Theory]
    [InlineData(FolderDeleteModeDto.MoveToParent)]
    [InlineData(FolderDeleteModeDto.DeleteContents)]
    public async Task Handle_ChangesStartWithTouchKeyRing(FolderDeleteModeDto mode)
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 1);

        await Handler().Handle(Delete(target, mode), TestContext.Current.CancellationToken);

        Assert.Equal(VaultChangeKind.TouchKeyRing, LastSubmit[0].Kind);
        Assert.Equal(RingETag, LastSubmit[0].ETag);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 1);
        NextWrite = Failed(write);

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 1);
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteFolder_SecondChunkConflicts_AuditsCommittedItems()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 60);
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 51, changes));

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(50, AuditLog.Entries.Count);
        Assert.All(AuditLog.Entries, entry => Assert.Equal(AuditEventType.SecretDeleted, entry.EventType));
        Assert.Equal(
            LastSubmit.Skip(1).Take(50).Select(change => change.ItemId.ToString("D")).Order(),
            AuditLog.Entries.Select(entry => entry.TargetId!).Order());
    }

    [Fact]
    public async Task DeleteFolder_MoveToParentSecondChunkConflicts_AuditsCommittedItems()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 60);
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 31, changes));

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.MoveToParent), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(30, AuditLog.Entries.Count);
        Assert.All(AuditLog.Entries, entry => Assert.Equal(AuditEventType.SecretMoved, entry.EventType));
        Assert.Equal(
            LastSubmit.Skip(1).Take(30).Select(change => change.ItemId.ToString("D")).Order(),
            AuditLog.Entries.Select(entry => entry.TargetId!).Order());
    }

    [Fact]
    public async Task DeleteFolder_FirstChunkConflicts_AuditsNothing()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 3);
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 0, changes));

        var result = await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task DeleteFolder_DeleteContentsRepeated_ReusesTheBatchId()
    {
        var target = NewFolder();
        var child = NewFolder(target.Id);
        GivenFolders(target, child);
        GivenSecretsIn(target.Id, 3);
        GivenSecretsIn(child.Id, 2);

        await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);
        await Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        var batches = Submitted
            .SelectMany(changes => changes)
            .Select(change => change.Secret?.DeletionBatchId ?? change.Folder?.DeletionBatchId)
            .Where(batch => batch is not null)
            .Distinct()
            .ToList();
        Assert.Single(batches);
        Assert.Equal(VaultIds.DeletionBatchIdFor(target.Id), batches[0]);
    }

    [Fact]
    public async Task DeleteFolder_DeleteContentsOfTwoFolders_UsesDifferentBatchIds()
    {
        var first = NewFolder();
        var second = NewFolder();
        GivenFolders(first, second);
        GivenSecretsIn(first.Id, 1);
        GivenSecretsIn(second.Id, 1);

        await Handler().Handle(Delete(first, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);
        await Handler().Handle(Delete(second, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);

        Assert.NotEqual(Submitted[0][1].Secret!.DeletionBatchId, Submitted[1][1].Secret!.DeletionBatchId);
    }

    [Fact]
    public async Task DeleteFolder_RingReadAfterConcurrentCommit_CannotUseStaleList()
    {
        var target = NewFolder();
        IReadOnlyList<Versioned<Folder>> current = [new(target, ETagOf(1))];
        var ringRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ringReleased = new TaskCompletionSource<Versioned<VaultKeyRing>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Store.GetKeyRingAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            ringRequested.TrySetResult();
            return ringReleased.Task;
        });
        Store.ListFoldersAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(current));
        GivenSecretsIn(target.Id, 0);

        var handling = Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken);
        await ringRequested.Task;
        current = [];
        ringReleased.SetResult(new Versioned<VaultKeyRing>(Ring, ETagOf(9)));
        var result = await handling;

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task DeleteFolder_SecondChunkStorageFails_AuditsCommittedItemsAndRethrows()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 60);
        var failure = new TimeoutException("storage down");
        GivenSubmitScript(changes => Interrupted(failure, 51, changes));

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(50, AuditLog.Entries.Count);
        Assert.Equal(
            LastSubmit.Skip(1).Take(50).Select(change => change.ItemId.ToString("D")).Order(),
            AuditLog.Entries.Select(entry => entry.TargetId!).Order());
    }

    [Fact]
    public async Task DeleteFolder_ClientAbortsBetweenChunks_AuditsCommittedItems()
    {
        var target = NewFolder();
        GivenFolders(target);
        GivenSecretsIn(target.Id, 60);
        GivenSubmitScript(changes => Interrupted(new OperationCanceledException(), 51, changes));

        await Assert.ThrowsAsync<OperationCanceledException>(() => Handler().Handle(Delete(target, FolderDeleteModeDto.DeleteContents), TestContext.Current.CancellationToken));

        Assert.Equal(50, AuditLog.Entries.Count);
    }
}
