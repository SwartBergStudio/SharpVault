// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Folders.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class MoveFolderHandlerTests : VaultHandlerTestBase
{
    private MoveFolderCommandHandler Handler() => new(Store, KeyRings, Audit, Current, Time, NullLogger<MoveFolderCommandHandler>.Instance);

    private Folder[] Chain(int length, Guid? under = null)
    {
        var folders = new List<Folder>();
        var parent = under;
        for (var level = 0; level < length; level++)
        {
            var folder = NewFolder(parent);
            folders.Add(folder);
            parent = folder.Id;
        }

        return [.. folders];
    }

    [Fact]
    public async Task Handle_IntoSelf_ReturnsRejectedCycle()
    {
        var folder = NewFolder();
        GivenFolders(folder);

        var result = await Handler().Handle(new MoveFolderCommand(folder.Id, folder.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderCycle, result.Error);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_IntoDescendant_ReturnsRejectedCycle()
    {
        var chain = Chain(3);
        GivenFolders(chain);

        var intoChild = await Handler().Handle(new MoveFolderCommand(chain[0].Id, chain[1].Id), TestContext.Current.CancellationToken);
        var intoGrandchild = await Handler().Handle(new MoveFolderCommand(chain[0].Id, chain[2].Id), TestContext.Current.CancellationToken);

        Assert.Equal(VaultMessages.FolderCycle, intoChild.Error);
        Assert.Equal(VaultMessages.FolderCycle, intoGrandchild.Error);
        Assert.Equal(ResultFailureKind.Rejected, intoChild.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_IntoSibling_Succeeds()
    {
        var first = NewFolder();
        var second = NewFolder();
        GivenFolders(first, second);

        var result = await Handler().Handle(new MoveFolderCommand(first.Id, second.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(second.Id, result.Value!.ParentId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_TooDeep_ReturnsRejectedDepth()
    {
        var target = Chain(8);
        var movable = NewFolder();
        var subtree = Chain(2, movable.Id);
        GivenFolders([.. target, movable, .. subtree]);

        var result = await Handler().Handle(new MoveFolderCommand(movable.Id, target[^1].Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderDepthExceeded, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_ExactlyAtMaxDepth_Succeeds()
    {
        var target = Chain(7);
        var movable = NewFolder();
        var subtree = Chain(2, movable.Id);
        GivenFolders([.. target, movable, .. subtree]);

        var result = await Handler().Handle(new MoveFolderCommand(movable.Id, target[^1].Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ToRoot_Succeeds()
    {
        var chain = Chain(3);
        GivenFolders(chain);

        var result = await Handler().Handle(new MoveFolderCommand(chain[2].Id, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ParentId);
        Assert.Null(LastSubmit[1].Folder!.ParentId);
    }

    [Fact]
    public async Task Handle_Move_TouchesTheKeyRingThenReplacesTheFolderAndAudits()
    {
        var first = NewFolder();
        var second = NewFolder();
        var folders = GivenFolders(first, second);
        NextWrite = WrittenWith(ETagOf(50), ETagOf(51));

        var result = await Handler().Handle(new MoveFolderCommand(first.Id, second.Id), TestContext.Current.CancellationToken);

        Assert.Equal([VaultChangeKind.TouchKeyRing, VaultChangeKind.ReplaceFolder], LastSubmit.Select(change => change.Kind));
        Assert.Equal(RingETag, LastSubmit[0].ETag);
        Assert.Equal(folders[0].ETag, LastSubmit[1].ETag);
        Assert.Equal(Now, LastSubmit[1].Folder!.ModifiedAt);
        Assert.True(result.IsSuccess);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.FolderMoved, entry.EventType);
        Assert.Equal(first.Id.ToString("D"), entry.TargetId);
        Assert.Null(entry.SecretId);
    }

    [Fact]
    public async Task Handle_SameParent_NoWrite()
    {
        var parent = NewFolder();
        var child = NewFolder(parent.Id);
        GivenFolders(parent, child);

        var result = await Handler().Handle(new MoveFolderCommand(child.Id, parent.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(parent.Id, result.Value!.ParentId);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_RootToRoot_NoWrite()
    {
        var root = NewFolder();
        GivenFolders(root);

        var result = await Handler().Handle(new MoveFolderCommand(root.Id, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_StoredCycle_ReturnsRejectedTreeInvalid()
    {
        var a = NewFolder();
        var b = NewFolder(a.Id);
        a.ParentId = b.Id;
        var movable = NewFolder();
        GivenFolders(a, b, movable);

        var result = await Handler().Handle(new MoveFolderCommand(movable.Id, a.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderTreeInvalid, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_Missing_ReturnsNotFound()
    {
        GivenFolders(NewFolder());

        var result = await Handler().Handle(new MoveFolderCommand(Guid.NewGuid(), null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_ParentMissing_ReturnsNotFound()
    {
        var folder = NewFolder();
        GivenFolders(folder);

        var result = await Handler().Handle(new MoveFolderCommand(folder.Id, Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ParentOfAnotherOwner_ReturnsNotFoundAndTreeUnchanged()
    {
        var folder = NewFolder();
        GivenFolders(folder);
        var foreign = GivenFolder(partition: Guid.NewGuid());

        var result = await Handler().Handle(new MoveFolderCommand(folder.Id, foreign.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Null(folder.ParentId);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        var folder = NewFolder();
        GivenFolders(folder);

        var result = await Handler().Handle(new MoveFolderCommand(folder.Id, null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var first = NewFolder();
        var second = NewFolder();
        GivenFolders(first, second);
        NextWrite = Failed(write);

        var result = await Handler().Handle(new MoveFolderCommand(first.Id, second.Id), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var first = NewFolder();
        var second = NewFolder();
        GivenFolders(first, second);
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new MoveFolderCommand(first.Id, second.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveFolder_RingReadAfterConcurrentCommit_CannotUseStaleList()
    {
        var first = NewFolder();
        var second = NewFolder();
        var secondMovedUnderFirst = NewFolder(first.Id);
        secondMovedUnderFirst.Id = second.Id;
        IReadOnlyList<Versioned<Folder>> current = [new(first, ETagOf(1)), new(second, ETagOf(2))];
        var ringRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ringReleased = new TaskCompletionSource<Versioned<VaultKeyRing>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Store.GetKeyRingAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            ringRequested.TrySetResult();
            return ringReleased.Task;
        });
        Store.ListFoldersAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(current));

        var handling = Handler().Handle(new MoveFolderCommand(first.Id, second.Id), TestContext.Current.CancellationToken);
        await ringRequested.Task;
        current = [new(first, ETagOf(1)), new(secondMovedUnderFirst, ETagOf(3))];
        ringReleased.SetResult(new Versioned<VaultKeyRing>(Ring, ETagOf(9)));
        var result = await handling;

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderCycle, result.Error);
        Assert.Empty(Submitted);
    }
}
