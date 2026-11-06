// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Application.Features.Vault.RecycleBin.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class PurgeRecycledSecretHandlerTests : VaultHandlerTestBase
{
    private PurgeRecycledSecretCommandHandler Handler() =>
        new(Store, EraserFactory.For(Store), Audit, Current, NullLogger<PurgeRecycledSecretCommandHandler>.Instance);

    private Versioned<Secret> Binned()
    {
        var secret = RecycledSecretIn(Guid.NewGuid(), null);
        return GivenBatch(secret.DeletionBatchId!.Value, [], [secret]).Secrets[0];
    }

    [Fact]
    public async Task Handle_DeletesBinRowAndVersions()
    {
        var binned = Binned();
        var versions = GivenVersions(binned.Value.Id, NewVersion(binned.Value), NewVersion(binned.Value), NewVersion(binned.Value));

        var result = await Handler().Handle(new PurgeRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [VaultChangeKind.DeleteVersion, VaultChangeKind.DeleteVersion, VaultChangeKind.DeleteVersion, VaultChangeKind.PurgeSecret],
            LastSubmit.Select(change => change.Kind));
        Assert.Equal(binned.ETag, LastSubmit[^1].ETag);
        Assert.Equal(versions.Select(version => version.ETag), LastSubmit.Take(3).Select(change => change.ETag));
        Assert.Equal(versions.Select(version => version.Value.Id), LastSubmit.Take(3).Select(change => change.Version!.Id));
        Assert.Single(Submitted);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretPurged, entry.EventType);
        Assert.Equal(binned.Value.Id, entry.SecretId);
        Assert.Equal(binned.Value.Id.ToString("D"), entry.TargetId);
        Assert.Equal(Owner, entry.OwnerId);
    }

    [Fact]
    public async Task Handle_NoVersions_PurgesOnlyTheBinRow()
    {
        var binned = Binned();
        GivenVersions(binned.Value.Id);

        var result = await Handler().Handle(new PurgeRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.PurgeSecret], LastSubmit.Select(change => change.Kind));
    }

    [Fact]
    public async Task Handle_NotInBin_ReturnsNotFound()
    {
        var result = await Handler().Handle(new PurgeRecycledSecretCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ActiveSecretId_ReturnsNotFoundAndKeepsIt()
    {
        var active = GivenSecret();

        var result = await Handler().Handle(new PurgeRecycledSecretCommand(active.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ItemInAnotherOwnersBin_ReturnsNotFoundAndKeepsIt()
    {
        var foreign = RecycledSecretIn(Guid.NewGuid(), null);
        Store.GetRecycledSecretAsync(Guid.NewGuid(), foreign.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Secret>?>(new Versioned<Secret>(foreign, ETagOf(1))));

        var result = await Handler().Handle(new PurgeRecycledSecretCommand(foreign.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        await Store.Received().GetRecycledSecretAsync(Owner, foreign.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var binned = Binned();
        GivenVersions(binned.Value.Id);
        NextWrite = Failed(write);

        var result = await Handler().Handle(new PurgeRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var binned = Binned();
        GivenVersions(binned.Value.Id);
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new PurgeRecycledSecretCommand(binned.Value.Id), TestContext.Current.CancellationToken));
    }
}
