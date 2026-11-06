// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Sharing.SharedItems.Queries;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

public sealed class GetSharedSecretHandlerTests : SharedItemsHandlerTestBase
{
    private GetSharedSecretQueryHandler Handler() => new(Store, SharedRings, Users, RealPolicy);

    [Theory]
    [InlineData(SharePermission.View, SharePermissionDto.View)]
    [InlineData(SharePermission.Edit, SharePermissionDto.Edit)]
    [InlineData(SharePermission.Manage, SharePermissionDto.Manage)]
    public async Task Handle_DirectGrant_ReturnsItemWithOwnerAndPermission(SharePermission granted, SharePermissionDto expected)
    {
        var secret = SharedSecret(name: "Visible name");
        DirectGrant(secret.Value.Id, granted);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.Permission);
        Assert.Equal(Sharer, result.Value.OwnerId);
        Assert.Equal(SharerName, result.Value.OwnerDisplayName);
        Assert.Equal("Visible name", result.Value.Item.Name);
        Assert.Equal(secret.ETag, result.Value.Item.ConcurrencyToken);
        Assert.True(result.Value.Item.HasValue);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecretInOwnersFolder_ForRecipientHidesOwnersFolderId()
    {
        var folder = SharedFolderRow();
        var secret = SharedSecret(folder.Id);
        DirectGrant(secret.Value.Id, SharePermission.View);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Item.FolderId);
        Assert.DoesNotContain(folder.Id.ToString("D"), JsonSerializer.Serialize(result.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Response_ContainsNoValueNotesOrHistory()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.Manage);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("shared-plain-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-plain-notes", json, StringComparison.Ordinal);
        Assert.Equal(0, StoreCalls("ListVersionsAsync"));
        Assert.Equal(0, StoreCalls("GetVersionAsync"));
        string[] allowed = ["Item", "OwnerDisplayName", "OwnerId", "Permission"];
        Assert.Equal(allowed, typeof(SharedSecretDto).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_BinnedSecretWithDirectGrantOnly_ReturnsNotFoundAfterReadingTheActiveRow()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.Manage);
        RecycleSecret(secret);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.True(StoreCalls("GetSecretAsync") >= 1);
        Assert.Equal(0, StoreCalls("GetRecycledSecretAsync"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_BinnedSecretWithFolderGrant_ReturnsNotFound()
    {
        var folder = SharedFolderRow();
        ActiveFolders(folder);
        FolderGrant(folder.Id, SharePermission.Manage);
        var secret = SharedSecret(folder.Id);
        RecycleSecret(secret);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OwnerIsCaller_ReturnsNotFoundWithoutAnyStoreRead()
    {
        var secret = GivenSecret();
        Store.ClearReceivedCalls();

        var result = await Handler().Handle(new GetSharedSecretQuery(Caller, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal(0, Sessions.Opened);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OwnerWithoutGrant_ReadsOnlyGrantRowsAndUnwrapsNothing()
    {
        var secret = SharedSecret();

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.All(StoreMethodsCalled(), method => Assert.Contains(method, GrantMethods));
        Assert.Equal(0, Sessions.Opened);
        Assert.Empty(Users.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_UngrantedExistingSecretMissingSecretAndOwnerIsCaller_ReturnIdenticalFailures()
    {
        var existing = SharedSecret();

        var ungranted = await Handler().Handle(new GetSharedSecretQuery(Sharer, existing.Value.Id), Token);
        var missing = await Handler().Handle(new GetSharedSecretQuery(Guid.NewGuid(), Guid.NewGuid()), Token);
        var ownerIsCaller = await Handler().Handle(new GetSharedSecretQuery(Caller, existing.Value.Id), Token);

        Assert.Equal(ungranted.FailureKind, missing.FailureKind);
        Assert.Equal(ungranted.Errors, missing.Errors);
        Assert.Equal(ungranted.Errors, ownerIsCaller.Errors);
        Assert.Equal(ungranted.FailureKind, ownerIsCaller.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantFromOneOwner_DoesNotOpenAnotherOwnersPartition()
    {
        var other = Guid.NewGuid();
        var granted = SharedSecret();
        DirectGrant(granted.Value.Id, SharePermission.Manage);

        var result = await Handler().Handle(new GetSharedSecretQuery(other, granted.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.DoesNotContain(Sharer, PartitionsRead());
        Assert.Equal(0, Sessions.Opened);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Granted_ReadsOnlyTheOwnersPartition()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);

        await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal([Sharer], PartitionsRead().ToList());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantWithNoPermission_ReturnsNotFound()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.None);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_FolderGrantOnGrandparent_ReturnsItemWithInheritedPermission()
    {
        var root = SharedFolderRow();
        var middle = SharedFolderRow(root.Id);
        var leaf = SharedFolderRow(middle.Id);
        ActiveFolders(root, middle, leaf);
        FolderGrant(root.Id, SharePermission.Edit);
        var secret = SharedSecret(leaf.Id);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(SharePermissionDto.Edit, result.Value!.Permission);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecretOutsideTheGrantedFolder_ReturnsNotFound()
    {
        var granted = SharedFolderRow();
        var other = SharedFolderRow();
        ActiveFolders(granted, other);
        FolderGrant(granted.Id, SharePermission.Manage);
        var secret = SharedSecret(other.Id);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderGrantOnlyAndUnfiledSecret_ReturnsNotFound()
    {
        var granted = SharedFolderRow();
        ActiveFolders(granted);
        FolderGrant(granted.Id, SharePermission.Manage);
        var secret = SharedSecret();

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_OwnerKeyRingMissing_ReturnsDamagedItemNotAnException()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);
        GivenSharerWithoutRing();

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Item.IsDamaged);
        Assert.Null(result.Value.Item.Name);
    }

    [Fact]
    public async Task Handle_OwnerKeyRingUnreadable_ReturnsDamagedItemNotAnException()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);
        GivenSharerRingUnreadable();

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Item.IsDamaged);
    }

    [Fact]
    public async Task Handle_DamagedMetadata_ReturnsDamagedItem()
    {
        var plain = DamagedSecret();
        plain.OwnerId = Sharer;
        var secret = GivenSecret(plain, partition: Sharer);
        DirectGrant(secret.Value.Id, SharePermission.View);

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Item.IsDamaged);
    }

    [Fact]
    public async Task Handle_OwnerUserRowMissing_UsesTheUnknownOwnerLabel()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);
        Users.GetAsync(Sharer, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<User>?>(null));

        var result = await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(SharingMessages.UnknownOwner, result.Value!.OwnerDisplayName);
    }

    [Fact]
    public async Task Handle_Success_DisposesTheOwnerSession()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);

        await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(1, Sessions.Opened);
        Assert.Equal(0, Sessions.Live);
    }

    [Fact]
    public async Task Handle_SecretMissingAfterGrant_DisposesTheOwnerSession()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);
        RecycleSecret(secret);

        await Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token);

        Assert.Equal(0, Sessions.Live);
    }

    [Fact]
    public async Task Handle_SecretReadFails_DisposesTheOwnerSession()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);
        Store.GetSecretAsync(Sharer, secret.Value.Id, Arg.Any<CancellationToken>())
            .Returns<Task<Versioned<Secret>?>>(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler().Handle(new GetSharedSecretQuery(Sharer, secret.Value.Id), Token));

        Assert.Equal(0, Sessions.Live);
    }

    [Fact]
    public void Validator_EmptyIds_AreRejected()
    {
        var validator = new GetSharedSecretQueryValidator();

        Assert.False(validator.Validate(new GetSharedSecretQuery(Guid.Empty, Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new GetSharedSecretQuery(Guid.NewGuid(), Guid.Empty)).IsValid);
        Assert.True(validator.Validate(new GetSharedSecretQuery(Guid.NewGuid(), Guid.NewGuid())).IsValid);
    }
}
