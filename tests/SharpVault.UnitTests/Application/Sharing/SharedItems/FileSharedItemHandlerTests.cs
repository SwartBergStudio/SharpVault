// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.SharedItems.Commands;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

public sealed class FileSharedItemHandlerTests : SharedItemsHandlerTestBase
{
    private readonly IShareInboxStore _inbox = Substitute.For<IShareInboxStore>();

    private readonly CollectingLogger<FileSharedItemCommandHandler> _logger = new();

    private FileSharedItemCommandHandler Handler() => new(_inbox, Store, Current, _logger);

    private (Guid ItemId, ShareInboxEntry Entry) GivenFilable(ShareItemKind kind = ShareItemKind.Secret, bool grant = true, bool entry = true)
    {
        var itemId = Guid.NewGuid();
        var row = ShareSamples.InboxEntry(Caller, Sharer, kind, itemId);
        _inbox.GetAsync(Caller, Sharer, kind, itemId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<ShareInboxEntry>?>(entry ? new Versioned<ShareInboxEntry>(row, "inbox-etag") : null));
        _inbox.SetFiledFolderAsync(Arg.Any<ShareInboxEntry>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(StoreWriteResult.Written));
        if (grant)
        {
            DirectGrant(itemId, SharePermission.View, kind);
        }

        return (itemId, row);
    }

    private Folder CallersFolder()
    {
        var folder = VaultSamples.Folder(Caller);
        Store.GetFolderAsync(Caller, folder.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(new Versioned<Folder>(folder, "f")));
        return folder;
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task Handle_FilesIntoAFolderOfTheCaller(ShareItemKind kind)
    {
        var (itemId, row) = GivenFilable(kind);
        var folder = CallersFolder();

        var result = await Handler().Handle(new FileSharedItemCommand(Sharer, kind, itemId, folder.Id), Token);

        Assert.True(result.IsSuccess);
        await _inbox.Received(1).SetFiledFolderAsync(Arg.Is<ShareInboxEntry>(entry => entry == row && entry.FiledFolderId == folder.Id), "inbox-etag", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullFolder_UnfilesWithoutReadingAnyFolder()
    {
        var (itemId, row) = GivenFilable();
        row.FiledFolderId = Guid.NewGuid();

        var result = await Handler().Handle(new FileSharedItemCommand(Sharer, ShareItemKind.Secret, itemId, null), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(row.FiledFolderId);
        Assert.Equal(0, StoreCalls("GetFolderAsync"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OwnerIsCaller_ReturnsNotFoundWithoutAnyRead()
    {
        var result = await Handler().Handle(new FileSharedItemCommand(Caller, ShareItemKind.Secret, Guid.NewGuid(), null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_inbox.ReceivedCalls());
        Assert.Empty(Store.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NoInboxEntry_ReturnsNotFoundAndWritesNothing()
    {
        var (itemId, _) = GivenFilable(entry: false);

        var result = await Handler().Handle(new FileSharedItemCommand(Sharer, ShareItemKind.Secret, itemId, null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await _inbox.DidNotReceive().SetFiledFolderAsync(Arg.Any<ShareInboxEntry>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantRevoked_ReturnsNotFoundAndWritesNothing()
    {
        var (itemId, _) = GivenFilable(grant: false);

        var result = await Handler().Handle(new FileSharedItemCommand(Sharer, ShareItemKind.Secret, itemId, null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await _inbox.DidNotReceive().SetFiledFolderAsync(Arg.Any<ShareInboxEntry>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderNotOwnedByTheCaller_ReturnsNotFoundAndLooksOnlyInTheCallersPartition()
    {
        var (itemId, _) = GivenFilable();
        var ownersFolder = SharedFolderRow();
        ActiveFolders(ownersFolder);

        var result = await Handler().Handle(new FileSharedItemCommand(Sharer, ShareItemKind.Secret, itemId, ownersFolder.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.DoesNotContain(Store.ReceivedCalls(), call => call.GetMethodInfo().Name == "GetFolderAsync" && (Guid)call.GetArguments()[0]! == Sharer);
        await _inbox.DidNotReceive().SetFiledFolderAsync(Arg.Any<ShareInboxEntry>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_UnrelatedOwnerAndMissingEverything_ReturnIdenticalFailures()
    {
        var (itemId, _) = GivenFilable(grant: false);

        var revoked = await Handler().Handle(new FileSharedItemCommand(Sharer, ShareItemKind.Secret, itemId, null), Token);
        var unknown = await Handler().Handle(new FileSharedItemCommand(Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), null), Token);
        var self = await Handler().Handle(new FileSharedItemCommand(Caller, ShareItemKind.Secret, itemId, null), Token);

        Assert.Equal(revoked.Errors, unknown.Errors);
        Assert.Equal(revoked.Errors, self.Errors);
        Assert.Equal(revoked.FailureKind, unknown.FailureKind);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    [InlineData(StoreWriteResult.AlreadyExists, ResultFailureKind.Conflict)]
    public async Task Handle_StoreOutcome_MapsToTheMatchingFailure(StoreWriteResult write, ResultFailureKind expected)
    {
        var (itemId, _) = GivenFilable();
        _inbox.SetFiledFolderAsync(Arg.Any<ShareInboxEntry>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(write));

        var result = await Handler().Handle(new FileSharedItemCommand(Sharer, ShareItemKind.Secret, itemId, null), Token);

        Assert.Equal(expected, result.FailureKind);
    }

    [Fact]
    public void Validator_EachRule_RejectsAndAccepts()
    {
        var validator = new FileSharedItemCommandValidator();

        Assert.True(validator.Validate(new FileSharedItemCommand(Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), null)).IsValid);
        Assert.True(validator.Validate(new FileSharedItemCommand(Guid.NewGuid(), ShareItemKind.Folder, Guid.NewGuid(), Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new FileSharedItemCommand(Guid.Empty, ShareItemKind.Secret, Guid.NewGuid(), null)).IsValid);
        Assert.False(validator.Validate(new FileSharedItemCommand(Guid.NewGuid(), ShareItemKind.Secret, Guid.Empty, null)).IsValid);
        Assert.False(validator.Validate(new FileSharedItemCommand(Guid.NewGuid(), (ShareItemKind)0, Guid.NewGuid(), null)).IsValid);
        Assert.False(validator.Validate(new FileSharedItemCommand(Guid.NewGuid(), (ShareItemKind)99, Guid.NewGuid(), null)).IsValid);
        Assert.False(validator.Validate(new FileSharedItemCommand(Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), Guid.Empty)).IsValid);
    }
}
