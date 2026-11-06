// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class MoveSecretHandlerTests : VaultHandlerTestBase
{
    private MoveSecretCommandHandler Handler() => new(Store, KeyRings, Policy, Audit, Time, NullLogger<MoveSecretCommandHandler>.Instance);

    private Versioned<Secret> SecretIn(Guid? folderId)
    {
        var secret = SecretWith("Movable");
        secret.FolderId = folderId;
        secret.Revision = 6;
        return GivenSecret(secret);
    }

    [Fact]
    public async Task Handle_SameFolder_NoWrite()
    {
        var folder = GivenFolder();
        var secret = SecretIn(folder.Value.Id);

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, folder.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(folder.Value.Id, result.Value!.FolderId);
        Assert.Equal(secret.ETag, result.Value.ConcurrencyToken);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
        Assert.Equal(6, secret.Value.Revision);
    }

    [Fact]
    public async Task Handle_RootToRoot_NoWrite()
    {
        var secret = SecretIn(null);

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_ToRoot_NoTouch()
    {
        var secret = SecretIn(Guid.NewGuid());
        NextWrite = WrittenWith(ETagOf(8));

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(LastSubmit);
        Assert.Equal(VaultChangeKind.ReplaceSecret, change.Kind);
        Assert.Equal(secret.ETag, change.ETag);
        Assert.Null(change.Secret!.FolderId);
        Assert.Null(result.Value!.FolderId);
        Assert.Equal(ETagOf(8), result.Value.ConcurrencyToken);
        await Store.DidNotReceive().GetFolderAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ToFolder_TouchesTargetFolder()
    {
        var target = GivenFolder();
        var secret = SecretIn(null);
        NextWrite = WrittenWith(ETagOf(7), ETagOf(8));

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, target.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.TouchFolder, VaultChangeKind.ReplaceSecret], LastSubmit.Select(change => change.Kind));
        Assert.Equal(target.Value.Id, LastSubmit[0].ItemId);
        Assert.Equal(target.ETag, LastSubmit[0].ETag);
        Assert.Equal(target.Value.Id, LastSubmit[1].Secret!.FolderId);
        Assert.Equal(ETagOf(8), result.Value!.ConcurrencyToken);
        Assert.Equal(target.Value.Id, result.Value.FolderId);
    }

    [Fact]
    public async Task Handle_Move_BumpsRevisionAndModifiedAtAndAudits()
    {
        var target = GivenFolder();
        var secret = SecretIn(null);

        await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, target.Value.Id), TestContext.Current.CancellationToken);

        var saved = LastSubmit[1].Secret!;
        Assert.Equal(7, saved.Revision);
        Assert.Equal(Now, saved.ModifiedAt);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretMoved, entry.EventType);
        Assert.Equal(secret.Value.Id, entry.SecretId);
        Assert.Equal(Owner, entry.OwnerId);
    }

    [Fact]
    public async Task Handle_TargetMissing_ReturnsNotFound()
    {
        var secret = SecretIn(null);

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_TargetFolderOfAnotherOwner_ReturnsNotFoundAndMovesNothing()
    {
        var foreign = GivenFolder(partition: Guid.NewGuid());
        var secret = SecretIn(null);

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, foreign.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
        await Store.Received().GetFolderAsync(Owner, foreign.Value.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StaleETag_ReturnsConflictWithoutSubmit()
    {
        var secret = SecretIn(Guid.NewGuid());

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, ETagOf(1), null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_StaleETagToSameFolder_StillReturnsConflict()
    {
        var secret = SecretIn(null);

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, ETagOf(1), null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
    }

    [Fact]
    public async Task Handle_SecretMissing_ReturnsNotFound()
    {
        var result = await Handler().Handle(new MoveSecretCommand(Guid.NewGuid(), ETagOf(1), null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        var secret = SecretIn(Guid.NewGuid());

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var secret = SecretIn(Guid.NewGuid());
        NextWrite = Failed(write);

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, null), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var secret = SecretIn(Guid.NewGuid());
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, null), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFoundWithoutStoreCall()
    {
        GivenNoGrant();
        var secret = SecretIn(Guid.NewGuid());

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantView_ReturnsNotFoundWithoutStoreCall()
    {
        GivenGrant(SecretPermission.View, Owner);
        var secret = SecretIn(Guid.NewGuid());

        var result = await Handler().Handle(new MoveSecretCommand(secret.Value.Id, secret.ETag, null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal([(secret.Value.Id, SecretPermission.Edit)], Policy.Requests);
    }
}
