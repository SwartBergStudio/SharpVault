// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Application.Features.Vault.RecycleBin.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class PurgeRecycledFolderHandlerTests : VaultHandlerTestBase
{
    private PurgeRecycledFolderCommandHandler Handler() =>
        new(Store, EraserFactory.For(Store), Audit, Current, NullLogger<PurgeRecycledFolderCommandHandler>.Instance);

    private (Folder Root, Folder Child, List<Secret> Secrets) SeedBatch(int secretsInRoot = 2, int secretsInChild = 1)
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        var secrets = Enumerable.Range(0, secretsInRoot).Select(_ => RecycledSecretIn(batch, root.Id))
            .Concat(Enumerable.Range(0, secretsInChild).Select(_ => RecycledSecretIn(batch, child.Id)))
            .ToList();
        GivenBatch(batch, [root, child], secrets);
        foreach (var secret in secrets)
        {
            GivenVersions(secret.Id);
        }

        return (root, child, secrets);
    }

    [Fact]
    public async Task Handle_PurgesSubtreeSecretsThenFolders()
    {
        var (root, child, secrets) = SeedBatch();

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(secrets.Count + 1, Submitted.Count);
        var secretSubmits = Submitted.Take(secrets.Count).ToList();
        Assert.All(secretSubmits, changes => Assert.Equal(VaultChangeKind.PurgeSecret, changes[0].Kind));
        Assert.Equal(secrets.Select(secret => secret.Id).Order(), secretSubmits.Select(changes => changes[0].Secret!.Id).Order());
        var folderSubmit = Submitted[^1];
        Assert.All(folderSubmit, change => Assert.Equal(VaultChangeKind.PurgeFolder, change.Kind));
        Assert.Equal([child.Id, root.Id], folderSubmit.Select(change => change.Folder!.Id));
    }

    [Fact]
    public async Task Handle_SecretVersionsAreDeletedWithTheirSecret()
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var secret = RecycledSecretIn(batch, root.Id);
        GivenBatch(batch, [root], [secret]);
        var versions = GivenVersions(secret.Id, NewVersion(secret), NewVersion(secret));

        await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(
            [VaultChangeKind.DeleteVersion, VaultChangeKind.DeleteVersion, VaultChangeKind.PurgeSecret],
            Submitted[0].Select(change => change.Kind));
        Assert.Equal(versions.Select(version => version.Value.Id), Submitted[0].Take(2).Select(change => change.Version!.Id));
    }

    [Fact]
    public async Task Handle_FolderWithoutSecrets_OnlyPurgesFolders()
    {
        var (root, _, _) = SeedBatch(0, 0);

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(Submitted);
        Assert.All(Submitted[0], change => Assert.Equal(VaultChangeKind.PurgeFolder, change.Kind));
    }

    [Fact]
    public async Task Handle_Subfolder_PurgesOnlyItsSubtree()
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var child = RecycledFolderIn(batch, root.Id);
        var inRoot = RecycledSecretIn(batch, root.Id);
        var inChild = RecycledSecretIn(batch, child.Id);
        GivenBatch(batch, [root, child], [inRoot, inChild]);
        GivenVersions(inChild.Id);

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(child.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, Submitted.Count);
        Assert.Equal(inChild.Id, Submitted[0][0].Secret!.Id);
        Assert.Equal(child.Id, Assert.Single(Submitted[1]).Folder!.Id);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Over1000_ReturnsRejected()
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var secrets = Enumerable.Range(0, VaultLimits.MaxItemsPerFolderOperation).Select(_ => RecycledSecretIn(batch, root.Id)).ToList();
        GivenBatch(batch, [root], secrets);

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderOperationTooLarge, result.Error);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_NotInBin_ReturnsNotFound()
    {
        var result = await Handler().Handle(new PurgeRecycledFolderCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ActiveFolderId_ReturnsNotFoundAndKeepsIt()
    {
        var active = GivenFolder();

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(active.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderInAnotherOwnersBin_ReturnsNotFound()
    {
        var foreign = RecycledFolderIn(Guid.NewGuid());
        Store.GetRecycledFolderAsync(Guid.NewGuid(), foreign.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(new Versioned<Folder>(foreign, ETagOf(1))));

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(foreign.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_StoredCycleInBatch_ReturnsRejectedTreeInvalid()
    {
        var batch = Guid.NewGuid();
        var a = RecycledFolderIn(batch);
        var b = RecycledFolderIn(batch, a.Id);
        a.ParentId = b.Id;
        GivenBatch(batch, [a, b], []);

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(a.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderTreeInvalid, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_Success_AuditsEverySecretAndFolder()
    {
        var (root, child, secrets) = SeedBatch();

        await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(secrets.Count + 2, AuditLog.Entries.Count);
        Assert.Equal(
            secrets.Select(secret => secret.Id).Order(),
            AuditLog.Entries.Where(entry => entry.EventType == AuditEventType.SecretPurged).Select(entry => entry.SecretId!.Value).Order());
        Assert.Equal(
            new[] { root.Id, child.Id }.Select(id => id.ToString("D")).Order(),
            AuditLog.Entries.Where(entry => entry.EventType == AuditEventType.FolderPurged).Select(entry => entry.TargetId!).Order());
    }

    [Fact]
    public async Task Handle_ASecretWriteConflicts_ReturnsConflictKeepsFoldersAndAuditsTheErasedSecrets()
    {
        var (root, _, secrets) = SeedBatch();
        var failing = secrets[1].Id;
        GivenSubmitScript(changes => changes[0].Secret?.Id == failing ? Failed(StoreWriteResult.Conflict) : null);

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.DoesNotContain(Submitted, changes => changes.Any(change => change.Kind == VaultChangeKind.PurgeFolder));
        Assert.Equal(secrets.Count, Submitted.Count);
        Assert.All(AuditLog.Entries, entry => Assert.Equal(AuditEventType.SecretPurged, entry.EventType));
        Assert.Equal(
            secrets.Where(secret => secret.Id != failing).Select(secret => secret.Id).Order(),
            AuditLog.Entries.Select(entry => entry.SecretId!.Value).Order());
    }

    [Fact]
    public async Task Handle_FolderWriteConflicts_ReturnsConflictAndAuditsOnlyTheErasedSecrets()
    {
        var (root, _, secrets) = SeedBatch();
        GivenSubmitScript(changes => changes[0].Kind == VaultChangeKind.PurgeFolder ? Failed(StoreWriteResult.Conflict) : null);

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(secrets.Count, AuditLog.Entries.Count);
        Assert.All(AuditLog.Entries, entry => Assert.Equal(AuditEventType.SecretPurged, entry.EventType));
    }

    [Fact]
    public async Task PurgeFolder_SecondFolderChunkConflicts_AuditsCommittedFolders()
    {
        var batch = Guid.NewGuid();
        var root = RecycledFolderIn(batch);
        var folders = new List<Folder> { root };
        folders.AddRange(Enumerable.Range(0, 119).Select(_ => RecycledFolderIn(batch, root.Id)));
        GivenBatch(batch, folders, []);
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 100, changes));

        var result = await Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(100, AuditLog.Entries.Count);
        Assert.All(AuditLog.Entries, entry => Assert.Equal(AuditEventType.FolderPurged, entry.EventType));
        Assert.Equal(
            LastSubmit.Take(100).Select(change => change.ItemId.ToString("D")).Order(),
            AuditLog.Entries.Select(entry => entry.TargetId!).Order());
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var (root, _, _) = SeedBatch();
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PurgeFolder_SecondFolderChunkConflicts_RepeatFinishes()
    {
        var batch = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childIds = Enumerable.Range(0, 119).Select(_ => Guid.NewGuid()).ToList();
        void GivenTree(HashSet<Guid> alreadyPurged)
        {
            var folders = new List<Folder>();
            if (!alreadyPurged.Contains(rootId))
            {
                var root = RecycledFolderIn(batch);
                root.Id = rootId;
                folders.Add(root);
            }

            foreach (var id in childIds.Where(id => !alreadyPurged.Contains(id)))
            {
                var child = RecycledFolderIn(batch, rootId);
                child.Id = id;
                folders.Add(child);
            }

            GivenBatch(batch, folders, []);
            foreach (var id in alreadyPurged)
            {
                Store.GetRecycledFolderAsync(Owner, id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(null));
            }
        }

        GivenTree(new HashSet<Guid>());
        GivenSubmitScript(changes => FailedAfter(StoreWriteResult.Conflict, 100, changes));

        var first = await Handler().Handle(new PurgeRecycledFolderCommand(rootId), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, first.FailureKind);
        var committed = Submitted[0].Take(100).Select(change => change.ItemId).ToHashSet();
        GivenTree(committed);
        GivenSubmitScript(_ => null);

        var second = await Handler().Handle(new PurgeRecycledFolderCommand(rootId), TestContext.Current.CancellationToken);

        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(childIds.Append(rootId).Order(), committed.Concat(Submitted[1].Select(change => change.ItemId)).Order());
    }

    [Fact]
    public async Task PurgeFolder_OneEraseThrows_OthersAuditedAndRethrown()
    {
        var (root, _, secrets) = SeedBatch();
        var failing = secrets[1].Id;
        var failure = new TimeoutException("storage down");
        GivenSubmitScript(changes => changes[0].Secret?.Id == failing ? throw failure : null);

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(
            secrets.Where(secret => secret.Id != failing).Select(secret => secret.Id).Order(),
            AuditLog.Entries.Select(entry => entry.SecretId!.Value).Order());
        Assert.All(AuditLog.Entries, entry => Assert.Equal(AuditEventType.SecretPurged, entry.EventType));
        Assert.DoesNotContain(Submitted, changes => changes.Any(change => change.Kind == VaultChangeKind.PurgeFolder));
    }

    [Fact]
    public async Task PurgeFolder_ClientAbortsDuringErase_ErasedSecretsAudited()
    {
        var (root, _, secrets) = SeedBatch();
        var aborting = secrets[2].Id;
        GivenSubmitScript(changes => changes[0].Secret?.Id == aborting ? throw new OperationCanceledException() : null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler().Handle(new PurgeRecycledFolderCommand(root.Id), TestContext.Current.CancellationToken));

        Assert.Equal(
            secrets.Where(secret => secret.Id != aborting).Select(secret => secret.Id).Order(),
            AuditLog.Entries.Select(entry => entry.SecretId!.Value).Order());
    }
}
