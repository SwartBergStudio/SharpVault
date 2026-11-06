// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Sharing.SharedItems.Queries;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

public sealed class GetSharedFolderHandlerTests : SharedItemsHandlerTestBase
{
    private GetSharedFolderQueryHandler Handler() => new(Store, SharedRings, Users, Current);

    private Task<Result<SharedFolderContentsDto>> Open(Guid owner, Guid folderId, string? token = null) =>
        Handler().Handle(new GetSharedFolderQuery(owner, folderId, 25, token), Token);

    private List<Folder> Chain(int length, out List<Guid> idsTopDown)
    {
        var folders = new List<Folder>();
        Guid? parent = null;
        for (var index = 0; index < length; index++)
        {
            var folder = SharedFolderRow(parent, "level-" + index);
            folders.Add(folder);
            parent = folder.Id;
        }

        ActiveFolders([.. folders]);
        idsTopDown = [.. folders.Select(folder => folder.Id)];
        return folders;
    }

    [Fact]
    public async Task Handle_GrantOnTheFolder_ReturnsContentsPermissionAndOwner()
    {
        var chain = Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.Edit);
        var secret = SharedSecret(ids[0], "Inside");
        Store.ListSecretsAsync(Sharer, Arg.Any<SecretFolderFilter>(), 25, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SecretPage([secret], "next")));

        var result = await Open(Sharer, ids[0]);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal(SharePermissionDto.Edit, dto.Permission);
        Assert.Equal("level-0", dto.Name);
        Assert.Equal(SharerName, dto.OwnerDisplayName);
        Assert.Equal(Sharer, dto.OwnerId);
        Assert.Equal("next", dto.ContinuationToken);
        var item = Assert.Single(dto.Items);
        Assert.Equal("Inside", item.Name);
        Assert.Null(item.FolderId);
        Assert.Equal(chain[0].Id, Assert.Single(dto.Path).FolderId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Listing_AsksTheStoreOnlyForTheRequestedFolder()
    {
        Chain(3, out var ids);
        FolderGrant(ids[0], SharePermission.View);

        await Open(Sharer, ids[2]);

        var filter = Assert.Single(Store.ReceivedCalls(), call => call.GetMethodInfo().Name == "ListSecretsAsync").GetArguments()[1];
        Assert.Equal(SecretFolderFilter.InFolder(ids[2]), filter);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_PathStartsAtTheHighestGrantedAncestor()
    {
        Chain(4, out var ids);
        FolderGrant(ids[1], SharePermission.View);

        var result = await Open(Sharer, ids[3]);

        Assert.True(result.IsSuccess);
        Assert.Equal([ids[1], ids[2], ids[3]], result.Value!.Path.Select(summary => summary.FolderId));
        Assert.Equal(["level-1", "level-2", "level-3"], result.Value.Path.Select(summary => summary.Name));
        Assert.DoesNotContain("level-0", JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
        Assert.DoesNotContain(ids[0].ToString("D"), JsonSerializer.Serialize(result.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_TwoGrantsInTheChain_PathStartsAtTheHigherAndPermissionIsTheBest()
    {
        Chain(4, out var ids);
        FolderGrant(ids[1], SharePermission.Manage);
        FolderGrant(ids[3], SharePermission.View);

        var result = await Open(Sharer, ids[3]);

        Assert.Equal(SharePermissionDto.Manage, result.Value!.Permission);
        Assert.Equal(ids[1], result.Value.Path[0].FolderId);
        Assert.Equal(3, result.Value.Path.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantOnlyOnADescendant_ReturnsNotFoundForTheAncestor()
    {
        Chain(3, out var ids);
        FolderGrant(ids[2], SharePermission.Manage);

        var result = await Open(Sharer, ids[0]);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, StoreCalls("ListSecretsAsync"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SiblingOfTheGrantedFolder_ReturnsNotFound()
    {
        var parent = SharedFolderRow();
        var granted = SharedFolderRow(parent.Id);
        var sibling = SharedFolderRow(parent.Id);
        ActiveFolders(parent, granted, sibling);
        FolderGrant(granted.Id, SharePermission.Manage);

        var result = await Open(Sharer, sibling.Id);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecretGrantOnly_ReturnsNotFoundForTheFolder()
    {
        Chain(1, out var ids);
        DirectGrant(ids[0], SharePermission.Manage, ShareItemKind.Secret);

        var result = await Open(Sharer, ids[0]);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NoGrantAtAll_ReturnsNotFound()
    {
        Chain(2, out var ids);

        var result = await Open(Sharer, ids[1]);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, StoreCalls("ListSecretsAsync"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantRevokedBetweenTwoCalls_SecondCallReturnsNotFound()
    {
        Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.View);

        var first = await Open(Sharer, ids[0]);
        RevokeFolderGrants();
        var second = await Open(Sharer, ids[0]);

        Assert.True(first.IsSuccess);
        Assert.Equal(ResultFailureKind.NotFound, second.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderBinned_ReturnsNotFoundEvenWithGrant()
    {
        Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.Manage);
        ActiveFolders();

        var result = await Open(Sharer, ids[0]);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantedAncestorBeyondABinnedLink_ReturnsNotFound()
    {
        var folders = Chain(3, out var ids);
        FolderGrant(ids[0], SharePermission.Manage);
        ActiveFolders(folders[0], folders[2]);

        var result = await Open(Sharer, ids[2]);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ParentCycle_EndsSafelyAndStaysWithinTheDepthLimit()
    {
        var first = SharedFolderRow();
        var second = SharedFolderRow(first.Id);
        first.ParentId = second.Id;
        ActiveFolders(first, second);
        FolderGrant(first.Id, SharePermission.View);

        var result = await Open(Sharer, second.Id);

        Assert.True(result.IsSuccess);
        Assert.InRange(result.Value!.Path.Count, 1, VaultLimits.MaxFolderDepth);
        Assert.All(result.Value.Path, summary => Assert.Contains(summary.FolderId, new[] { first.Id, second.Id }));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SelfParentedGrantlessFolder_ReturnsNotFoundWithoutLooping()
    {
        var loop = SharedFolderRow();
        loop.ParentId = loop.Id;
        ActiveFolders(loop);
        FolderGrant(Guid.NewGuid(), SharePermission.Manage);

        var result = await Open(Sharer, loop.Id);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantExactlyAtTheDepthLimit_IsReached()
    {
        Chain(VaultLimits.MaxFolderDepth, out var ids);
        FolderGrant(ids[0], SharePermission.View);

        var result = await Open(Sharer, ids[^1]);

        Assert.True(result.IsSuccess);
        Assert.Equal(VaultLimits.MaxFolderDepth, result.Value!.Path.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantBeyondTheDepthLimit_ReturnsNotFound()
    {
        Chain(VaultLimits.MaxFolderDepth + 1, out var ids);
        FolderGrant(ids[0], SharePermission.View);

        var result = await Open(Sharer, ids[^1]);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_SubFolders_AreDirectChildrenOnlyAndSortedByName()
    {
        var root = SharedFolderRow(name: "Root");
        var zebra = SharedFolderRow(root.Id, "zebra");
        var alpha = SharedFolderRow(root.Id, "Alpha");
        var grandchild = SharedFolderRow(zebra.Id, "grandchild");
        var unrelated = SharedFolderRow(null, "unrelated");
        ActiveFolders(root, zebra, alpha, grandchild, unrelated);
        FolderGrant(root.Id, SharePermission.View);

        var result = await Open(Sharer, root.Id);

        Assert.Equal(["Alpha", "zebra"], result.Value!.SubFolders.Select(summary => summary.Name));
    }

    [Fact]
    public async Task Handle_FolderNameUndecryptable_ReturnsDamagedSummariesNotAnException()
    {
        Chain(2, out var ids);
        FolderGrant(ids[0], SharePermission.View);
        GivenSharerWithoutRing();

        var result = await Open(Sharer, ids[1]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDamaged);
        Assert.Null(result.Value.Name);
        Assert.All(result.Value.Path, summary => Assert.True(summary.IsDamaged));
    }

    [Fact]
    public async Task Handle_OwnerKeyRingUnreadable_ReturnsDamagedFolder()
    {
        Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.View);
        GivenSharerRingUnreadable();

        var result = await Open(Sharer, ids[0]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDamaged);
    }

    [Fact]
    public async Task Handle_OneFolderNameDamaged_OthersStillDecrypt()
    {
        var root = SharedFolderRow(name: "Good root");
        var broken = SharedFolderRow(root.Id, "Broken");
        broken.NameEnvelope = root.NameEnvelope;
        ActiveFolders(root, broken);
        FolderGrant(root.Id, SharePermission.View);

        var result = await Open(Sharer, root.Id);

        Assert.Equal("Good root", result.Value!.Name);
        Assert.True(Assert.Single(result.Value.SubFolders).IsDamaged);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OwnerIsCaller_ReturnsNotFoundWithoutAnyStoreRead()
    {
        Store.ClearReceivedCalls();

        var result = await Open(Caller, Guid.NewGuid());

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal(0, Sessions.Opened);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OwnerWithoutGrant_ReadsNoFolderRowsOfThatOwner()
    {
        Chain(2, out var ids);
        Store.ClearReceivedCalls();

        var result = await Open(Sharer, ids[1]);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.DoesNotContain("ListFoldersAsync", StoreMethodsCalled());
        Assert.DoesNotContain("ListSecretsAsync", StoreMethodsCalled());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OwnerWithoutGrant_DoesNotUnwrapTheOwnersKeyOrReadTheKeyRing()
    {
        Chain(2, out var ids);
        Store.ClearReceivedCalls();

        await Open(Sharer, ids[1]);

        Assert.Equal(0, Sessions.Opened);
        Assert.DoesNotContain("GetKeyRingAsync", StoreMethodsCalled());
        Assert.Empty(Users.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_UngrantedAndNonexistentOwner_ReturnIdenticalFailures()
    {
        Chain(1, out var ids);

        var ungranted = await Open(Sharer, ids[0]);
        var missing = await Open(Guid.NewGuid(), Guid.NewGuid());
        var ownerIsCaller = await Open(Caller, ids[0]);

        Assert.Equal(ungranted.FailureKind, missing.FailureKind);
        Assert.Equal(ungranted.Errors, missing.Errors);
        Assert.Equal(ungranted.Errors, ownerIsCaller.Errors);
    }

    [Fact]
    public async Task Handle_InvalidContinuationTokenFromTheStore_IsRejected()
    {
        Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.View);
        Store.ListSecretsAsync(Sharer, Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<SecretPage>>(_ => throw new InvalidContinuationTokenException());

        var result = await Open(Sharer, ids[0], ContinuationTokens.ForStorage("x"));

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.InvalidContinuationToken, result.Error);
    }

    [Fact]
    public async Task Handle_OwnerUserRowMissing_UsesTheUnknownOwnerLabel()
    {
        Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.View);
        Users.GetAsync(Sharer, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<User>?>(null));

        var result = await Open(Sharer, ids[0]);

        Assert.Equal(SharingMessages.UnknownOwner, result.Value!.OwnerDisplayName);
    }

    [Fact]
    public async Task Handle_Success_DisposesTheOwnerSession()
    {
        Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.View);

        await Open(Sharer, ids[0]);

        Assert.Equal(1, Sessions.Opened);
        Assert.Equal(0, Sessions.Live);
    }

    [Fact]
    public async Task Handle_NoGrant_DisposesWhateverSessionWasOpened()
    {
        Chain(1, out var ids);

        await Open(Sharer, ids[0]);

        Assert.Equal(0, Sessions.Live);
    }

    [Fact]
    public async Task Handle_SecretListFails_DisposesTheOwnerSession()
    {
        Chain(1, out var ids);
        FolderGrant(ids[0], SharePermission.View);
        Store.ListSecretsAsync(Sharer, Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<SecretPage>>(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(Sharer, ids[0]));

        Assert.Equal(0, Sessions.Live);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SharingLimits.MaxPageSize + 1)]
    public void Validator_PageSizeOutOfRange_IsRejected(int pageSize)
    {
        var validator = new GetSharedFolderQueryValidator();

        Assert.False(validator.Validate(new GetSharedFolderQuery(Guid.NewGuid(), Guid.NewGuid(), pageSize, null)).IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(SharingLimits.MaxPageSize)]
    public void Validator_PageSizeInRange_IsAccepted(int pageSize)
    {
        var validator = new GetSharedFolderQueryValidator();

        Assert.True(validator.Validate(new GetSharedFolderQuery(Guid.NewGuid(), Guid.NewGuid(), pageSize, null)).IsValid);
    }

    [Fact]
    public void Validator_EmptyIdsAndForeignTokens_AreRejected()
    {
        var validator = new GetSharedFolderQueryValidator();

        Assert.False(validator.Validate(new GetSharedFolderQuery(Guid.Empty, Guid.NewGuid(), 10, null)).IsValid);
        Assert.False(validator.Validate(new GetSharedFolderQuery(Guid.NewGuid(), Guid.Empty, 10, null)).IsValid);
        Assert.False(validator.Validate(new GetSharedFolderQuery(Guid.NewGuid(), Guid.NewGuid(), 10, "not a token")).IsValid);
        Assert.True(validator.Validate(new GetSharedFolderQuery(Guid.NewGuid(), Guid.NewGuid(), 10, ContinuationTokens.ForStorage("next-1"))).IsValid);
    }
}
