// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.RecycleBin.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

[Trait("Category", "Security")]
public sealed class PurgeGrantCascadeTests : VaultHandlerTestBase
{
    private const int GrantCount = 150;

    private readonly GrantCascadeWorld _world = new();
    private readonly IShareInboxStore _inbox = Substitute.For<IShareInboxStore>();

    private PurgeRecycledSecretCommandHandler SecretHandler()
    {
        _world.Wire(Store, _inbox);
        return new(Store, EraserFactory.For(Store, _inbox), Audit, Current, NullLogger<PurgeRecycledSecretCommandHandler>.Instance);
    }

    private PurgeRecycledFolderCommandHandler FolderHandler()
    {
        _world.Wire(Store, _inbox);
        return new(Store, EraserFactory.For(Store, _inbox), Audit, Current, NullLogger<PurgeRecycledFolderCommandHandler>.Instance);
    }

    private Versioned<Secret> SecretWithGrants(int grants)
    {
        var binned = GivenRecycledSecret();
        GivenVersions(binned.Value.Id);
        _world.AddGrants(Owner, ShareItemKind.Secret, binned.Value.Id, grants);
        return binned;
    }

    private List<AuditEntry> Audited(AuditEventType type) => [.. AuditLog.Entries.Where(entry => entry.EventType == type)];

    [Fact]
    public async Task PurgeSecret_SecondChunkInterrupted_AuditsCommittedGrantsRethrowsAndRepeatFinishes()
    {
        var binned = SecretWithGrants(GrantCount);
        _world.FailChunkNumber = 2;
        _world.FailWith = new TimeoutException("storage went away");
        var handler = SecretHandler();
        var command = new PurgeRecycledSecretCommand(binned.Value.Id);

        await Assert.ThrowsAsync<TimeoutException>(() => handler.Handle(command, TestContext.Current.CancellationToken));

        Assert.Equal(GrantCascadeWorld.ChunkSize, Audited(AuditEventType.ShareErased).Count);
        Assert.Empty(Audited(AuditEventType.SecretPurged));
        Assert.False(_world.IsErased(binned.Value.Id));
        Assert.Equal(GrantCount - GrantCascadeWorld.ChunkSize, _world.Grants.Count);
        Assert.Equal(GrantCount - GrantCascadeWorld.ChunkSize, _world.InboxRows);

        _world.ResetFailure();
        var repeat = await handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(repeat.IsSuccess);
        Assert.True(_world.IsErased(binned.Value.Id));
        Assert.Empty(_world.Grants);
        Assert.Equal(0, _world.InboxRows);
        var erased = Audited(AuditEventType.ShareErased);
        Assert.Equal(GrantCount, erased.Count);
        Assert.Equal(GrantCount, erased.Select(entry => entry.RecipientId).Distinct().Count());
        Assert.Single(Audited(AuditEventType.SecretPurged));
    }

    [Fact]
    public async Task PurgeSecret_SecondChunkConflicts_AuditsCommittedGrantsAndReturnsConflict()
    {
        var binned = SecretWithGrants(GrantCount);
        _world.FailChunkNumber = 2;
        var handler = SecretHandler();
        var command = new PurgeRecycledSecretCommand(binned.Value.Id);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(GrantCascadeWorld.ChunkSize, Audited(AuditEventType.ShareErased).Count);
        Assert.Empty(Audited(AuditEventType.SecretPurged));
        Assert.False(_world.IsErased(binned.Value.Id));

        _world.ResetFailure();
        Assert.True((await handler.Handle(command, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(GrantCount, Audited(AuditEventType.ShareErased).Count);
        Assert.Empty(_world.Grants);
        Assert.Equal(0, _world.InboxRows);
    }

    [Fact]
    public async Task PurgeSecret_InboxRemovalFails_PurgeStillSucceeds()
    {
        var binned = SecretWithGrants(3);
        var handler = SecretHandler();
        _inbox.RemoveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("inbox down"));

        var result = await handler.Handle(new PurgeRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, Audited(AuditEventType.ShareErased).Count);
    }

    [Fact]
    public async Task PurgeSecret_GrantsPrecedeVersionsAndSecretIsLast()
    {
        var binned = SecretWithGrants(2);
        GivenVersions(binned.Value.Id, NewVersion(binned.Value));
        var handler = SecretHandler();
        GivenSubmitScript(changes => _world.Submit(changes));

        await handler.Handle(new PurgeRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(
            [VaultChangeKind.DeleteShareGrant, VaultChangeKind.DeleteShareGrant, VaultChangeKind.DeleteVersion, VaultChangeKind.PurgeSecret],
            Submitted.Single().Select(change => change.Kind));
    }

    [Fact]
    public async Task PurgeSecret_NoGrants_ReadsGrantsOnceAndSendsOnlyTheSecret()
    {
        var binned = SecretWithGrants(0);
        var handler = SecretHandler();
        GivenSubmitScript(changes => _world.Submit(changes));

        await handler.Handle(new PurgeRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        await Store.Received(1).ListShareGrantsForItemAsync(Owner, ShareItemKind.Secret, binned.Value.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await Store.DidNotReceive().GetShareGrantByRecipientETagAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Equal(VaultChangeKind.PurgeSecret, Assert.Single(Assert.Single(Submitted)).Kind);
        Assert.Empty(Audited(AuditEventType.ShareErased));
    }

    [Fact]
    public async Task PurgeFolder_SecretWithManyGrantsInterrupted_AuditsCommittedGrantsRethrowsAndRepeatFinishes()
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var secret = RecycledSecretIn(batch, root.Id);
        GivenBatch(batch, [root], [secret]);
        GivenVersions(secret.Id);
        _world.AddGrants(Owner, ShareItemKind.Secret, secret.Id, GrantCount);
        _world.FailChunkNumber = 2;
        _world.FailWith = new TimeoutException("storage went away");
        var handler = FolderHandler();
        var command = new PurgeRecycledFolderCommand(root.Id);

        await Assert.ThrowsAsync<TimeoutException>(() => handler.Handle(command, TestContext.Current.CancellationToken));

        Assert.Equal(GrantCascadeWorld.ChunkSize, Audited(AuditEventType.ShareErased).Count);
        Assert.Empty(Audited(AuditEventType.SecretPurged));
        Assert.Empty(Audited(AuditEventType.FolderPurged));
        Assert.False(_world.IsErased(secret.Id));

        _world.ResetFailure();
        var repeat = await handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(repeat.IsSuccess);
        Assert.True(_world.IsErased(secret.Id));
        Assert.True(_world.IsErased(root.Id));
        Assert.Empty(_world.Grants);
        Assert.Equal(0, _world.InboxRows);
        Assert.Equal(GrantCount, Audited(AuditEventType.ShareErased).Count);
        Assert.Single(Audited(AuditEventType.SecretPurged));
        Assert.Single(Audited(AuditEventType.FolderPurged));
    }

    [Fact]
    public async Task PurgeFolder_GrantsAndFoldersSplitAcrossChunks_AuditsExactlyTheCommittedItems()
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        GivenBatch(batch, [root, child], []);
        _world.AddGrants(Owner, ShareItemKind.Folder, root.Id, GrantCascadeWorld.ChunkSize - 1);
        _world.FailChunkNumber = 2;

        var result = await FolderHandler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(GrantCascadeWorld.ChunkSize - 1, Audited(AuditEventType.ShareErased).Count);
        var purged = Assert.Single(Audited(AuditEventType.FolderPurged));
        Assert.Equal(child.Id.ToString("D"), purged.TargetId);
        Assert.True(_world.IsErased(child.Id));
        Assert.False(_world.IsErased(root.Id));
    }
}
