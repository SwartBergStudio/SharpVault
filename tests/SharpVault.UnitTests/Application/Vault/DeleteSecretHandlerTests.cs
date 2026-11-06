// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Domain.Audit;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class DeleteSecretHandlerTests : VaultHandlerTestBase
{
    private DeleteSecretCommandHandler Handler() => new(Store, Policy, Audit, Time, NullLogger<DeleteSecretCommandHandler>.Instance);

    [Fact]
    public async Task Handle_Owner_RecyclesWithBatchIdAndDeletedAt()
    {
        var secret = GivenSecret();

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(LastSubmit);
        Assert.Equal(VaultChangeKind.RecycleSecret, change.Kind);
        Assert.Equal(secret.ETag, change.ETag);
        Assert.Equal(Now, change.Secret!.DeletedAt);
        Assert.NotNull(change.Secret.DeletionBatchId);
        Assert.NotEqual(Guid.Empty, change.Secret.DeletionBatchId);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretDeleted, entry.EventType);
        Assert.Equal(secret.Value.Id, entry.SecretId);
        Assert.Equal(Owner, entry.OwnerId);
    }

    [Fact]
    public async Task Handle_TwoDeletes_GetDifferentBatchIds()
    {
        var first = GivenSecret();
        var second = GivenSecret();

        await Handler().Handle(new DeleteSecretCommand(first.Value.Id, first.ETag), TestContext.Current.CancellationToken);
        await Handler().Handle(new DeleteSecretCommand(second.Value.Id, second.ETag), TestContext.Current.CancellationToken);

        Assert.NotEqual(Submitted[0].Single().Secret!.DeletionBatchId, Submitted[1].Single().Secret!.DeletionBatchId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantIsEdit_ReturnsNotFoundAndWritesNothing()
    {
        GivenGrant(SecretPermission.Edit, Owner);
        var secret = GivenSecret();

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
        Assert.Equal([(secret.Value.Id, SecretPermission.Owner)], Policy.Requests);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantIsManage_ReturnsNotFoundAndWritesNothing()
    {
        GivenGrant(SecretPermission.Manage, Owner);
        var secret = GivenSecret();

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFoundWithoutStoreCall()
    {
        GivenNoGrant();
        var secret = GivenSecret();

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_StaleETag_ReturnsConflict()
    {
        var secret = GivenSecret();

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, ETagOf(1)), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
        Assert.Null(secret.Value.DeletedAt);
    }

    [Fact]
    public async Task Handle_Missing_ReturnsNotFound()
    {
        var result = await Handler().Handle(new DeleteSecretCommand(Guid.NewGuid(), ETagOf(1)), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var secret = GivenSecret();
        NextWrite = Failed(write);

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var secret = GivenSecret();
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecretOfAnotherOwner_ReturnsNotFound()
    {
        var secret = GivenSecret(partition: Guid.NewGuid());

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await Store.DidNotReceive().SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteSecret_RequestAbortedAfterCommit_StillWritesAudit()
    {
        var secret = GivenSecret();
        using var abort = new CancellationTokenSource();
        Store.SubmitAsync(Owner, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                Record(changes);
                abort.Cancel();
                return Task.FromResult(WrittenWith([.. changes.Select(_ => NextETag())]));
            });

        var result = await Handler().Handle(new DeleteSecretCommand(secret.Value.Id, secret.ETag), abort.Token);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretDeleted, entry.EventType);
    }
}
