// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

public sealed class SharedPathsHandlerTests : SharedItemsHandlerTestBase
{
    private RevealSecretFieldCommandHandler Revealer() => new(Store, Encryptor, RealPolicy, Audit);

    private UpdateSecretCommandHandler Updater() => new(Store, KeyRings, Encryptor, RealPolicy, Audit, Time, NullLogger<UpdateSecretCommandHandler>.Instance);

    private Task<Result<RevealedValueDto>> Reveal(Guid secretId, Guid? sharedOwner, RevealFieldDto field = RevealFieldDto.Value) =>
        Revealer().Handle(new RevealSecretFieldCommand(secretId, field, RevealIntentDto.View, sharedOwner), Token);

    private Task<Result<SecretDto>> Update(Versioned<Secret> secret, Guid? sharedOwner, string? etag = null, bool replaceValue = true) =>
        Updater().Handle(
            new UpdateSecretCommand(secret.Value.Id, etag ?? secret.ETag, SecretTypeDto.Login, "Edited name", "edited-user", null, [], replaceValue, replaceValue ? "edited-value" : null, false, null, sharedOwner),
            Token);

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_SharedOwner_UsesOwnerPartitionAndAuditsRecipientAsActorAndOwnerAsOwner()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);

        var result = await Reveal(secret.Value.Id, Sharer);

        Assert.True(result.IsSuccess);
        Assert.Equal("shared-plain-value", result.Value!.Value);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretValueRevealed, entry.EventType);
        Assert.Equal(Caller, entry.ActorId);
        Assert.Equal(Sharer, entry.OwnerId);
        Assert.Equal(secret.Value.Id, entry.SecretId);
        Assert.DoesNotContain(Caller, PartitionsRead());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_SharedNotes_AuditsTheNotesEvent()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);

        var result = await Reveal(secret.Value.Id, Sharer, RevealFieldDto.Notes);

        Assert.Equal("shared-plain-notes", result.Value!.Value);
        Assert.Equal(AuditEventType.SecretNotesRevealed, Assert.Single(AuditLog.Entries).EventType);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_SharedAuditFails_ReturnsNoValue()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);
        AuditLog.Failure = new TimeoutException("audit down");

        Result<RevealedValueDto>? result = null;
        await Assert.ThrowsAsync<TimeoutException>(async () => result = await Reveal(secret.Value.Id, Sharer));

        Assert.Null(result);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_SharedAuditEntry_ContainsNoPlaintext()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);

        await Reveal(secret.Value.Id, Sharer);

        var json = JsonSerializer.Serialize(AuditLog.Entries);
        Assert.DoesNotContain("shared-plain-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-plain-notes", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_BinnedSecretWithDirectGrantOnly_ReturnsNotFoundWithoutUnwrapOrAudit()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.Manage);
        RecycleSecret(secret);

        var result = await Reveal(secret.Value.Id, Sharer);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_BinnedSecretWithFolderGrant_ReturnsNotFoundWithoutAudit()
    {
        var folder = SharedFolderRow();
        ActiveFolders(folder);
        FolderGrant(folder.Id, SharePermission.Manage);
        var secret = SharedSecret(folder.Id);
        RecycleSecret(secret);

        var result = await Reveal(secret.Value.Id, Sharer);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_SharedOwnerIsCaller_ReturnsNotFoundWithoutReadsOrAudit()
    {
        var secret = GivenSecret();
        Store.ClearReceivedCalls();

        var result = await Reveal(secret.Value.Id, Caller);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Empty(AuditLog.Entries);
        Assert.Equal(0, Encryptor.OpenCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_SharedOwnerWithoutGrant_ReturnsNotFoundReadingOnlyGrantRows()
    {
        var secret = SharedSecret();
        Store.ClearReceivedCalls();

        var result = await Reveal(secret.Value.Id, Sharer);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.All(StoreMethodsCalled(), method => Assert.Contains(method, GrantMethods));
        Assert.Empty(AuditLog.Entries);
        Assert.Equal(0, Encryptor.OpenCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_OwnersSecretIdUnderAnotherOwnersRoute_ReturnsNotFound()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);

        var result = await Reveal(secret.Value.Id, Guid.NewGuid());

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_ViewOnlyRecipient_ReturnsNotFoundWithoutSubmitOrAudit()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.View);

        var result = await Update(secret, Sharer);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Theory]
    [InlineData(SharePermission.Edit)]
    [InlineData(SharePermission.Manage)]
    [Trait("Category", "Security")]
    public async Task Update_EditOrManageRecipient_WritesToTheOwnerPartitionAndAuditsRecipientAsActor(SharePermission granted)
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, granted);

        var result = await Update(secret, Sharer);

        Assert.True(result.IsSuccess);
        await Store.Received(1).SubmitAsync(Sharer, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
        await Store.DidNotReceive().SubmitAsync(Caller, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretUpdated, entry.EventType);
        Assert.Equal(Caller, entry.ActorId);
        Assert.Equal(Sharer, entry.OwnerId);
        Assert.Equal("Edited name", result.Value!.Name);
        Assert.Null(result.Value.FolderId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_SharedEdit_ArchivesTheOldValueUnderTheOwnersId()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.Edit);

        await Update(secret, Sharer);

        Assert.Equal([VaultChangeKind.ReplaceSecret, VaultChangeKind.AddVersion], LastSubmit.Select(change => change.Kind));
        Assert.Equal(Sharer, LastSubmit[0].Secret!.OwnerId);
        Assert.Equal(Sharer, LastSubmit[1].Version!.OwnerId);
        Assert.Equal(secret.Value.Id, LastSubmit[1].Version!.SecretId);
        Assert.Equal("edited-value", DecryptCurrentValue(LastSubmit[0].Secret!));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_SharedResponse_HasNoHistoryOrVersionIdAndNoOwnerFolder()
    {
        var folder = SharedFolderRow();
        var secret = SharedSecret(folder.Id);
        DirectGrant(secret.Value.Id, SharePermission.Edit);

        var result = await Update(secret, Sharer);

        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(folder.Id.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("edited-value", json, StringComparison.Ordinal);
        Assert.Equal(0, StoreCalls("GetVersionAsync"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_SharedStaleETag_ReturnsConflictWithoutSubmit()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.Edit);

        var result = await Update(secret, Sharer, etag: ETagOf(1));

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_BinnedSecretWithDirectGrantOnly_ReturnsNotFoundWithoutSubmit()
    {
        var secret = SharedSecret();
        DirectGrant(secret.Value.Id, SharePermission.Manage);
        RecycleSecret(secret);

        var result = await Update(secret, Sharer);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_SharedOwnerIsCaller_ReturnsNotFoundWithoutReadsOrWrites()
    {
        var secret = GivenSecret();
        Store.ClearReceivedCalls();

        var result = await Update(secret, Caller);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_SharedOwnerWithoutGrant_ReturnsNotFoundWithoutWrites()
    {
        var secret = SharedSecret();

        var result = await Update(secret, Sharer);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_FolderEditGrantOnParent_AllowsTheEdit()
    {
        var folder = SharedFolderRow();
        ActiveFolders(folder);
        FolderGrant(folder.Id, SharePermission.Edit);
        var secret = SharedSecret(folder.Id);

        var result = await Update(secret, Sharer);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Update_FolderViewGrantOnParent_IsRefused()
    {
        var folder = SharedFolderRow();
        ActiveFolders(folder);
        FolderGrant(folder.Id, SharePermission.View);
        var secret = SharedSecret(folder.Id);

        var result = await Update(secret, Sharer);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }
}
