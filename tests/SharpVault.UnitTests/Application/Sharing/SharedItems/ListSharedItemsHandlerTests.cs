// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.SharedItems.Queries;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

public sealed class ListSharedItemsHandlerTests : SharedItemsHandlerTestBase
{
    private readonly IShareInboxStore _inbox = Substitute.For<IShareInboxStore>();

    private ListSharedItemsQueryHandler Handler() => new(_inbox, Store, SharedRings, Users, Current);

    private Task<Result<SharedItemPageDto>> List(Guid? filed = null, int pageSize = 50, string? token = null) =>
        Handler().Handle(new ListSharedItemsQuery(filed, pageSize, token), Token);

    private void InboxPage(string? next, params ShareInboxEntry[] entries) =>
        _inbox.ListAsync(Caller, Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ShareInboxPage([.. entries.Select(entry => new Versioned<ShareInboxEntry>(entry, "e"))], next)));

    private void GrantsBetween(Guid owner, params ShareGrant[] grants) =>
        Store.ListGrantsToRecipientBetweenAsync(owner, Caller, Arg.Any<ShareItemRef>(), Arg.Any<ShareItemRef>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ShareGrant>>(grants));

    private Guid NewOwner()
    {
        var owner = Guid.NewGuid();
        Store.GetKeyRingAsync(owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(Ring, RingETag)));
        Store.GetSecretAsync(owner, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Secret>?>(null));
        Store.GetFolderAsync(owner, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(null));
        GrantsBetween(owner);
        return owner;
    }

    private (ShareInboxEntry Entry, ShareGrant Grant, Versioned<Secret> Row) SecretOf(Guid owner, SharePermission permission = SharePermission.View, string name = "Listed")
    {
        var row = SharedSecret(name: name, ownerId: owner);
        return (ShareSamples.InboxEntry(Caller, owner, ShareItemKind.Secret, row.Value.Id), ShareSamples.Grant(owner, ShareItemKind.Secret, row.Value.Id, Caller, permission), row);
    }

    private (ShareInboxEntry Entry, ShareGrant Grant, Folder Row) FolderOf(Guid owner, string name = "Listed folder")
    {
        var folder = VaultSamples.Folder(owner);
        SecretMetadataCipher.EncryptFolderName(MetadataSession, folder, name);
        Store.GetFolderAsync(owner, folder.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(new Versioned<Folder>(folder, "f")));
        return (ShareSamples.InboxEntry(Caller, owner, ShareItemKind.Folder, folder.Id), ShareSamples.Grant(owner, ShareItemKind.Folder, folder.Id, Caller, SharePermission.View), folder);
    }

    [Fact]
    public async Task Handle_EmptyInbox_ReturnsEmptyPageWithoutReadingAnyVault()
    {
        InboxPage(null);

        var result = await List();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Null(result.Value.ContinuationToken);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal(0, Sessions.Opened);
    }

    [Fact]
    public async Task Handle_TwoOwners_UnwrapsOncePerOwnerAndDisposesBoth()
    {
        var first = NewOwner();
        var second = NewOwner();
        var a1 = SecretOf(first, name: "A1");
        var a2 = SecretOf(first, name: "A2");
        var b1 = SecretOf(second, SharePermission.Edit, "B1");
        GrantsBetween(first, a1.Grant, a2.Grant);
        GrantsBetween(second, b1.Grant);
        InboxPage("next", a1.Entry, b1.Entry, a2.Entry);

        var result = await List();

        Assert.True(result.IsSuccess);
        Assert.Equal(["A1", "B1", "A2"], result.Value!.Items.Select(item => item.Name));
        Assert.Equal(2, Sessions.Opened);
        Assert.Equal(0, Sessions.Live);
        Assert.Equal(1, Store.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "GetKeyRingAsync" && call.GetArguments()[0] is Guid g && g == first));
        Assert.Equal(1, Store.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "ListGrantsToRecipientBetweenAsync" && call.GetArguments()[0] is Guid g && g == second));
        Assert.Equal("next", result.Value.ContinuationToken);
        Assert.Equal(SharePermissionDto.Edit, result.Value.Items[1].Permission);
        Assert.Equal(first, result.Value.Items[0].OwnerId);
        Assert.Equal(SharerName, result.Value.Items[0].OwnerDisplayName);
    }

    [Fact]
    public async Task Handle_Items_CarryOnlyTheListFields()
    {
        var owner = NewOwner();
        var row = SecretOf(owner, name: "Mine");
        GrantsBetween(owner, row.Grant);
        InboxPage(null, row.Entry);

        var result = await List();

        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("shared-plain-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-plain-notes", json, StringComparison.Ordinal);
        Assert.DoesNotContain("shared.test", json, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-tag", json, StringComparison.Ordinal);
        string[] expected = ["FiledFolderId", "IsDamaged", "ItemId", "Kind", "Name", "OwnerDisplayName", "OwnerId", "Permission", "SharedAt", "Type", "Username"];
        Assert.Equal(expected, typeof(SharedItemDto).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Handle_FolderEntry_ShowsNameAndNoType()
    {
        var owner = NewOwner();
        var folder = FolderOf(owner, "Team folder");
        GrantsBetween(owner, folder.Grant);
        InboxPage(null, folder.Entry);

        var result = await List();

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(SharedItemKindDto.Folder, item.Kind);
        Assert.Equal("Team folder", item.Name);
        Assert.Null(item.Type);
        Assert.Null(item.Username);
    }

    [Fact]
    public async Task Handle_OwnerWithoutKeyRing_MarksItsRowsDamagedWithoutAnError()
    {
        var owner = NewOwner();
        Store.GetKeyRingAsync(owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));
        var secret = SecretOf(owner);
        var folder = FolderOf(owner);
        GrantsBetween(owner, secret.Grant, folder.Grant);
        InboxPage(null, secret.Entry, folder.Entry);

        var result = await List();

        Assert.True(result.IsSuccess);
        Assert.All(result.Value!.Items, item => Assert.True(item.IsDamaged));
        Assert.All(result.Value.Items, item => Assert.Null(item.Name));
    }

    [Fact]
    public async Task Handle_OwnerKeyRingUnreadable_MarksRowsDamagedAndKeepsOtherOwners()
    {
        var broken = NewOwner();
        var healthy = NewOwner();
        var bad = SecretOf(broken);
        var good = SecretOf(healthy, name: "Healthy");
        GrantsBetween(broken, bad.Grant);
        GrantsBetween(healthy, good.Grant);
        InboxPage(null, bad.Entry, good.Entry);
        Store.GetKeyRingAsync(broken, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(VaultSamples.KeyRing(broken), "r")));

        var result = await List();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Items.Count(item => item.IsDamaged));
        Assert.Equal(1, result.Value.Items.Count(item => !item.IsDamaged));
    }

    [Fact]
    public async Task Handle_SecretWithDamagedMetadata_IsMarkedDamaged()
    {
        var owner = NewOwner();
        var plain = DamagedSecret();
        plain.OwnerId = owner;
        var row = GivenSecret(plain, partition: owner);
        var grant = ShareSamples.Grant(owner, ShareItemKind.Secret, row.Value.Id, Caller, SharePermission.View);
        GrantsBetween(owner, grant);
        InboxPage(null, ShareSamples.InboxEntry(Caller, owner, ShareItemKind.Secret, row.Value.Id));

        var result = await List();

        Assert.True(Assert.Single(result.Value!.Items).IsDamaged);
    }

    [Fact]
    public async Task Handle_ReadFails_DisposesEveryOpenedOwnerSession()
    {
        var first = NewOwner();
        var second = NewOwner();
        var a = SecretOf(first);
        var b = SecretOf(second);
        GrantsBetween(first, a.Grant);
        GrantsBetween(second, b.Grant);
        Store.GetSecretAsync(second, b.Row.Value.Id, Arg.Any<CancellationToken>()).Returns<Task<Versioned<Secret>?>>(_ => throw new InvalidOperationException("boom"));
        InboxPage(null, a.Entry, b.Entry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => List());

        Assert.Equal(2, Sessions.Opened);
        Assert.Equal(0, Sessions.Live);
    }

    private async Task<IReadOnlyList<ShareGrant>> FailOnceTheFirstOwnerIsOpenAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Sessions.Opened < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Yield();
        }

        throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Handle_SecondOwnerFailsWhileOpening_DisposesTheFirstOwnersSession()
    {
        var first = NewOwner();
        var second = NewOwner();
        var a = SecretOf(first);
        var b = SecretOf(second);
        GrantsBetween(first, a.Grant);
        Store.ListGrantsToRecipientBetweenAsync(second, Caller, Arg.Any<ShareItemRef>(), Arg.Any<ShareItemRef>(), Arg.Any<CancellationToken>())
            .Returns(_ => FailOnceTheFirstOwnerIsOpenAsync());
        InboxPage(null, a.Entry, b.Entry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => List());

        Assert.True(Sessions.Opened >= 1);
        Assert.Equal(0, Sessions.Live);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_InboxRowWithoutGrant_IsDroppedAndNeverReadsTheItem()
    {
        var owner = NewOwner();
        var orphan = SecretOf(owner);
        GrantsBetween(owner);
        InboxPage(null, orphan.Entry);

        var result = await List();

        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, StoreCalls("GetSecretAsync"));
        Assert.Equal(0, Sessions.Live);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantForAnotherRecipientOrWithNoPermission_IsDropped()
    {
        var owner = NewOwner();
        var foreign = SecretOf(owner, name: "Foreign");
        foreign.Grant.RecipientId = Guid.NewGuid();
        var none = SecretOf(owner, SharePermission.None, "None");
        var valid = SecretOf(owner, name: "Valid");
        GrantsBetween(owner, foreign.Grant, none.Grant, valid.Grant);
        InboxPage(null, foreign.Entry, none.Entry, valid.Entry);

        var result = await List();

        Assert.Equal(["Valid"], result.Value!.Items.Select(item => item.Name));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_BinnedSecretAndFolder_AreDropped()
    {
        var owner = NewOwner();
        var binnedSecret = SecretOf(owner, name: "Binned");
        Store.GetSecretAsync(owner, binnedSecret.Row.Value.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Secret>?>(null));
        var binnedFolder = FolderOf(owner, "Binned folder");
        Store.GetFolderAsync(owner, binnedFolder.Row.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(null));
        var live = SecretOf(owner, name: "Live");
        GrantsBetween(owner, binnedSecret.Grant, binnedFolder.Grant, live.Grant);
        InboxPage(null, binnedSecret.Entry, binnedFolder.Entry, live.Entry);

        var result = await List();

        Assert.Equal(["Live"], result.Value!.Items.Select(item => item.Name));
        Assert.Equal(0, StoreCalls("GetRecycledSecretAsync"));
    }

    [Fact]
    public async Task Handle_EveryRowDropped_ReturnsEmptyPageButKeepsTheContinuationToken()
    {
        var owner = NewOwner();
        var orphan = SecretOf(owner);
        GrantsBetween(owner);
        InboxPage("keep-going", orphan.Entry);

        var result = await List();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal("keep-going", result.Value.ContinuationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_InboxRowNamingTheCallerAsOwner_IsDroppedWithoutReadingTheCallersVault()
    {
        var own = ShareSamples.InboxEntry(Caller, Caller, ShareItemKind.Secret, Guid.NewGuid());
        InboxPage(null, own);

        var result = await List();

        Assert.Empty(result.Value!.Items);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal(0, Sessions.Opened);
    }

    [Fact]
    public async Task Handle_FiledFolderFilterAndPageSize_PassThroughToTheInbox()
    {
        var filed = Guid.NewGuid();
        InboxPage(null);

        await List(filed, 7, null);

        await _inbox.Received(1).ListAsync(Caller, filed, 7, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FiledFolderPointingAtADeletedFolder_KeepsTheStaleIdAndNeverReadsTheCallersFolders()
    {
        var owner = NewOwner();
        var row = SecretOf(owner);
        row.Entry.FiledFolderId = Guid.NewGuid();
        GrantsBetween(owner, row.Grant);
        InboxPage(null, row.Entry);

        var result = await List();

        Assert.Equal(row.Entry.FiledFolderId, Assert.Single(result.Value!.Items).FiledFolderId);
        Assert.DoesNotContain(Caller, PartitionsRead());
    }

    [Fact]
    public async Task Handle_InboxRejectsTheToken_ReturnsRejected()
    {
        _inbox.ListAsync(Caller, Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ShareInboxPage>>(_ => throw new InvalidContinuationTokenException());

        var result = await List(token: ContinuationTokens.ForStorage("stale"));

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharpVault.Application.Features.Sharing.Common.SharingMessages.InvalidContinuationToken, result.Error);
    }

    [Fact]
    public async Task Handle_GrantRange_CoversFoldersFirstThenSecretsInKeyOrder()
    {
        var owner = NewOwner();
        var secretA = SecretOf(owner);
        var secretB = SecretOf(owner);
        var folder = FolderOf(owner);
        GrantsBetween(owner, secretA.Grant, secretB.Grant, folder.Grant);
        InboxPage(null, secretA.Entry, secretB.Entry, folder.Entry);

        await List();

        var call = Assert.Single(Store.ReceivedCalls(), c => c.GetMethodInfo().Name == "ListGrantsToRecipientBetweenAsync");
        var first = (ShareItemRef)call.GetArguments()[2]!;
        var last = (ShareItemRef)call.GetArguments()[3]!;
        var secretIds = new[] { secretA.Row.Value.Id, secretB.Row.Value.Id }.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToList();
        Assert.Equal(new ShareItemRef(ShareItemKind.Folder, folder.Row.Id), first);
        Assert.Equal(new ShareItemRef(ShareItemKind.Secret, secretIds[1]), last);
    }

    [Fact]
    public async Task Handle_ManyRows_ReadsEachGrantedItemOnceAndNothingElse()
    {
        var owner = NewOwner();
        var rows = Enumerable.Range(0, 20).Select(_ => SecretOf(owner)).ToList();
        GrantsBetween(owner, [.. rows.Select(row => row.Grant)]);
        InboxPage(null, [.. rows.Select(row => row.Entry)]);

        var result = await List();

        Assert.Equal(20, result.Value!.Items.Count);
        Assert.Equal(20, StoreCalls("GetSecretAsync"));
        Assert.Equal(1, StoreCalls("GetKeyRingAsync"));
        Assert.Equal(1, StoreCalls("ListGrantsToRecipientBetweenAsync"));
        Assert.Equal(0, StoreCalls("ListSecretsAsync"));
        Assert.Equal(0, StoreCalls("ListFoldersAsync"));
    }

    [Fact]
    public async Task Handle_OwnerUserRowMissing_UsesTheUnknownOwnerLabel()
    {
        var owner = NewOwner();
        Users.GetAsync(owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<SharpVault.Domain.Users.User>?>(null));
        var row = SecretOf(owner);
        GrantsBetween(owner, row.Grant);
        InboxPage(null, row.Entry);

        var result = await List();

        Assert.Equal(SharpVault.Application.Features.Sharing.Common.SharingMessages.UnknownOwner, Assert.Single(result.Value!.Items).OwnerDisplayName);
    }

    [Fact]
    public void Validator_PageSizeAndIdsAndToken_AreChecked()
    {
        var validator = new ListSharedItemsQueryValidator();

        Assert.False(validator.Validate(new ListSharedItemsQuery(null, 0, null)).IsValid);
        Assert.False(validator.Validate(new ListSharedItemsQuery(null, ListSharedItemsQuery.MaxPageSize + 1, null)).IsValid);
        Assert.True(validator.Validate(new ListSharedItemsQuery(null, ListSharedItemsQuery.MaxPageSize, null)).IsValid);
        Assert.False(validator.Validate(new ListSharedItemsQuery(Guid.Empty, 10, null)).IsValid);
        Assert.False(validator.Validate(new ListSharedItemsQuery(null, 10, "garbage")).IsValid);
        Assert.True(validator.Validate(new ListSharedItemsQuery(Guid.NewGuid(), 10, ContinuationTokens.ForStorage("next-1"))).IsValid);
    }
}
