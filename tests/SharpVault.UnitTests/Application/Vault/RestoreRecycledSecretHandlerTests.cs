// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.RecycleBin.Commands;
using SharpVault.Domain.Audit;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RestoreRecycledSecretHandlerTests : VaultHandlerTestBase
{
    private RestoreRecycledSecretCommandHandler Handler() =>
        new(Store, KeyRings, Audit, Current, Time, NullLogger<RestoreRecycledSecretCommandHandler>.Instance);

    private Versioned<SharpVault.Domain.Vaults.Secret> BinnedIn(Guid? folderId)
    {
        var secret = RecycledSecretIn(Guid.NewGuid(), folderId);
        secret.Revision = 9;
        return GivenBatch(secret.DeletionBatchId!.Value, [], [secret]).Secrets[0];
    }

    [Fact]
    public async Task Handle_FolderActive_RestoresIntoFolderWithTouch()
    {
        var folder = GivenFolder();
        var binned = BinnedIn(folder.Value.Id);
        NextWrite = WrittenWith(ETagOf(60), ETagOf(61));

        var result = await Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.TouchFolder, VaultChangeKind.RestoreSecret], LastSubmit.Select(change => change.Kind));
        Assert.Equal(folder.Value.Id, LastSubmit[0].ItemId);
        Assert.Equal(folder.ETag, LastSubmit[0].ETag);
        Assert.Equal(binned.ETag, LastSubmit[1].ETag);
        var restored = LastSubmit[1].Secret!;
        Assert.Equal(folder.Value.Id, restored.FolderId);
        Assert.Null(restored.DeletedAt);
        Assert.Null(restored.DeletionBatchId);
        Assert.Equal(folder.Value.Id, result.Value!.FolderId);
        Assert.Equal(ETagOf(61), result.Value.ConcurrencyToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderGone_RestoresToRoot()
    {
        var binned = BinnedIn(Guid.NewGuid());

        var result = await Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(LastSubmit);
        Assert.Equal(VaultChangeKind.RestoreSecret, change.Kind);
        Assert.Null(change.Secret!.FolderId);
        Assert.Null(result.Value!.FolderId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OriginalFolderBelongsToAnotherOwner_RestoresToRoot()
    {
        var foreign = GivenFolder(partition: Guid.NewGuid());
        var binned = BinnedIn(foreign.Value.Id);

        var result = await Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(LastSubmit.Single().Secret!.FolderId);
    }

    [Fact]
    public async Task Handle_Unfiled_RestoresWithoutTouchOrFolderLookup()
    {
        var binned = BinnedIn(null);

        await Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal([VaultChangeKind.RestoreSecret], LastSubmit.Select(change => change.Kind));
        await Store.DidNotReceive().GetFolderAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Restore_BumpsRevisionAndModifiedAtAndAudits()
    {
        var binned = BinnedIn(null);

        await Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        var restored = LastSubmit.Single().Secret!;
        Assert.Equal(10, restored.Revision);
        Assert.Equal(Now, restored.ModifiedAt);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretRestored, entry.EventType);
        Assert.Equal(binned.Value.Id, entry.SecretId);
        Assert.Equal(binned.Value.Id.ToString("D"), entry.TargetId);
        Assert.Equal(Owner, entry.OwnerId);
    }

    [Fact]
    public async Task Handle_NotInBin_ReturnsNotFound()
    {
        var result = await Handler().Handle(new RestoreRecycledSecretCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ActiveSecretId_ReturnsNotFound()
    {
        var active = GivenSecret();

        var result = await Handler().Handle(new RestoreRecycledSecretCommand(active.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ItemInAnotherOwnersBin_ReturnsNotFound()
    {
        var foreign = RecycledSecretIn(Guid.NewGuid(), null);
        Store.GetRecycledSecretAsync(Guid.NewGuid(), foreign.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<SharpVault.Domain.Vaults.Secret>?>(new Versioned<SharpVault.Domain.Vaults.Secret>(foreign, ETagOf(1))));

        var result = await Handler().Handle(new RestoreRecycledSecretCommand(foreign.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await Store.Received().GetRecycledSecretAsync(Owner, foreign.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        var binned = BinnedIn(null);

        var result = await Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var binned = BinnedIn(null);
        NextWrite = Failed(write);

        var result = await Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var binned = BinnedIn(null);
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new RestoreRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken));
    }
}
