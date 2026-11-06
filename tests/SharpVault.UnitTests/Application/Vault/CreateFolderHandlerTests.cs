// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Folders.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class CreateFolderHandlerTests : VaultHandlerTestBase
{
    private CreateFolderCommandHandler Handler() => new(Store, KeyRings, Audit, Current, Time, NullLogger<CreateFolderCommandHandler>.Instance);

    private Folder[] Chain(int length)
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

    private void GivenFolderCount(int count, Folder? parent = null)
    {
        var rows = Enumerable.Range(0, count).Select(_ => VaultSamples.Folder(Owner)).ToList();
        if (parent is not null)
        {
            rows[0] = parent;
        }

        GivenFolders([.. rows]);
    }

    [Fact]
    public async Task Handle_RootLevel_SubmitsTouchKeyRingThenAddFolder()
    {
        GivenFolders();
        NextWrite = WrittenWith(ETagOf(20), ETagOf(21));

        var result = await Handler().Handle(new CreateFolderCommand("Finance", null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.TouchKeyRing, VaultChangeKind.AddFolder], LastSubmit.Select(change => change.Kind));
        Assert.Equal(RingETag, LastSubmit[0].ETag);
        var folder = LastSubmit[1].Folder!;
        Assert.Null(folder.ParentId);
        Assert.Equal(Owner, folder.OwnerId);
        Assert.Equal(7, folder.Id.Version);
        Assert.Equal(Now, folder.CreatedAt);
        Assert.Equal(Now, folder.ModifiedAt);
        Assert.Equal("Finance", result.Value!.Name);
        Assert.Equal(folder.Id, result.Value.Id);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.FolderCreated, entry.EventType);
        Assert.Equal(folder.Id.ToString("D"), entry.TargetId);
        Assert.Null(entry.SecretId);
        Assert.Equal(Owner, entry.OwnerId);
    }

    [Fact]
    public async Task Handle_UnderParent_SetsParentId()
    {
        var parent = NewFolder();
        GivenFolders(parent);

        var result = await Handler().Handle(new CreateFolderCommand("Child", parent.Id), TestContext.Current.CancellationToken);

        Assert.Equal(parent.Id, LastSubmit[1].Folder!.ParentId);
        Assert.Equal(parent.Id, result.Value!.ParentId);
    }

    [Fact]
    public async Task Handle_NameIsEncryptedTrimmedAndBoundToTheFolder()
    {
        GivenFolders();

        await Handler().Handle(new CreateFolderCommand("  Finance  ", null), TestContext.Current.CancellationToken);

        var folder = LastSubmit[1].Folder!;
        Assert.Equal("Finance", MetadataSession.Decrypt(new EncryptionContext(VaultPurposes.FolderName, folder.Id, Owner), folder.NameEnvelope));
        Assert.Equal(Ring.MetadataKeyVersion, folder.MetadataKeyVersion);
    }

    [Fact]
    public async Task Handle_ParentMissing_ReturnsNotFound()
    {
        GivenFolders();

        var result = await Handler().Handle(new CreateFolderCommand("Child", Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ParentOfAnotherOwner_ReturnsNotFound()
    {
        GivenFolders();
        var foreign = GivenFolder(partition: Guid.NewGuid());

        var result = await Handler().Handle(new CreateFolderCommand("Child", foreign.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ParentAtDepth10_ReturnsRejectedDepth()
    {
        var chain = Chain(VaultLimits.MaxFolderDepth);
        GivenFolders(chain);

        var result = await Handler().Handle(new CreateFolderCommand("Too deep", chain[^1].Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderDepthExceeded, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_ParentAtDepth9_Succeeds()
    {
        var chain = Chain(VaultLimits.MaxFolderDepth - 1);
        GivenFolders(chain);

        var result = await Handler().Handle(new CreateFolderCommand("Tenth level", chain[^1].Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_1000Folders_ReturnsRejectedLimit()
    {
        GivenFolderCount(VaultLimits.MaxFoldersPerVault);

        var result = await Handler().Handle(new CreateFolderCommand("One too many", null), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderLimitReached, result.Error);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_999Folders_Succeeds()
    {
        GivenFolderCount(VaultLimits.MaxFoldersPerVault - 1);

        var result = await Handler().Handle(new CreateFolderCommand("The thousandth", null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_FirstEverFolder_CreatesKeyRing()
    {
        GivenNoKeyRing();
        GivenFolders();
        VaultKeyRing? added = null;
        Store.AddKeyRingAsync(Arg.Do<VaultKeyRing>(ring => added = ring), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(call.Arg<VaultKeyRing>(), ETagOf(30))));

        var result = await Handler().Handle(new CreateFolderCommand("First", null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(Owner, added.OwnerId);
        Assert.Equal(ETagOf(30), LastSubmit[0].ETag);
        Assert.Equal(VaultChangeKind.TouchKeyRing, LastSubmit[0].Kind);
        Assert.Equal("First", result.Value!.Name);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_StoredCycleUnderParent_ReturnsRejectedTreeInvalid()
    {
        var a = NewFolder();
        var b = NewFolder(a.Id);
        a.ParentId = b.Id;
        GivenFolders(a, b);

        var result = await Handler().Handle(new CreateFolderCommand("Child", a.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.FolderTreeInvalid, result.Error);
        Assert.Empty(Submitted);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.AlreadyExists, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        GivenFolders();
        NextWrite = Failed(write);

        var result = await Handler().Handle(new CreateFolderCommand("Finance", null), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        GivenFolders();
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new CreateFolderCommand("Finance", null), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditEntry_ContainsNoFolderName()
    {
        GivenFolders();
        var marker = "SV-FOLDER-" + Guid.NewGuid().ToString("N");

        await Handler().Handle(new CreateFolderCommand(marker, null), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(marker, JsonSerializer.Serialize(AuditLog.Entries), StringComparison.Ordinal);
        var folder = LastSubmit[1].Folder!;
        Assert.False(folder.NameEnvelope.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(marker)) >= 0);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Command_ToString_ReturnsTypeNameOnly()
    {
        Assert.Equal(nameof(CreateFolderCommand), new CreateFolderCommand("secret-name", null).ToString());
    }

    [Fact]
    public async Task CreateFolder_RingReadAfterConcurrentCommit_CannotUseStaleList()
    {
        var parent = NewFolder();
        IReadOnlyList<Versioned<Folder>> current = [new(parent, ETagOf(1))];
        var ringRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ringReleased = new TaskCompletionSource<Versioned<VaultKeyRing>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Store.GetKeyRingAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            ringRequested.TrySetResult();
            return ringReleased.Task;
        });
        Store.ListFoldersAsync(Owner, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(current));

        var handling = Handler().Handle(new CreateFolderCommand("Child", parent.Id), TestContext.Current.CancellationToken);
        await ringRequested.Task;
        current = [];
        ringReleased.SetResult(new Versioned<VaultKeyRing>(Ring, ETagOf(9)));
        var result = await handling;

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_UnreadableKeyRing_FailsClosedWithoutWriting()
    {
        GivenFolders();
        GivenUnreadableKeyRing();

        await Assert.ThrowsAsync<SharpVault.Application.Common.Exceptions.SecretDecryptionException>(() =>
            Handler().Handle(new CreateFolderCommand("Child", null), TestContext.Current.CancellationToken));

        Assert.Empty(Submitted);
    }
}
