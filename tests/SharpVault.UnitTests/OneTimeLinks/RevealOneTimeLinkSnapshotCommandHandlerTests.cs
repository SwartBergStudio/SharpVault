// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UnitTests.Application.Vault;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class RevealOneTimeLinkSnapshotCommandHandlerTests : VaultHandlerTestBase
{
    private const string SnapshotValue = "the-value";
    private const string SnapshotNotes = "the-notes";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private RevealOneTimeLinkSnapshotCommandHandler Handler(bool enabled = true, ICurrentUser? requester = null)
    {
        var who = requester ?? Current;
        return new(
            OneTimeLinkSamples.Policy(enabled),
            new ShareManagementGate(Store, new ShareAccessResolver(Store, who), who),
            Store,
            KeyRings,
            Encryptor,
            OneTimeLinkSamples.AuditWriter(AuditLog, Time),
            who,
            new StubRequestMetadata());
    }

    private Versioned<Secret> GivenSecretWithFields(string? notes = SnapshotNotes)
    {
        var secret = SecretWith("Snapshot", "snapshot-user", "https://snapshot.test/", "tag-a");
        secret.ValueEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, Owner), SnapshotValue);
        secret.NotesEnvelope = notes is null
            ? null
            : SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretNotes, secret.Id, Owner), notes);
        return GivenSecret(secret);
    }

    private void GivenNoShares()
    {
        Store.GetShareGrantAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<ShareGrant>?>(null));
        Store.ListFolderGrantsToRecipientAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ShareGrant>>([]));
    }

    private void GivenShare(Guid recipient, Guid secretId, SharePermission permission)
    {
        GivenNoShares();
        Store.GetShareGrantAsync(Owner, ShareItemKind.Secret, secretId, recipient, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<ShareGrant>?>(new Versioned<ShareGrant>(
                new ShareGrant
                {
                    OwnerId = Owner,
                    ItemKind = ShareItemKind.Secret,
                    ItemId = secretId,
                    RecipientId = recipient,
                    Permission = permission,
                },
                NextETag())));
    }

    [Fact]
    public async Task Handle_OwnerWithTheSecret_ReturnsTheSnapshotAndAuditsWithTheRequester()
    {
        var secret = GivenSecretWithFields();

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("Snapshot", result.Value!.Name);
        Assert.Equal("snapshot-user", result.Value.Username);
        Assert.Equal("https://snapshot.test/", result.Value.Url);
        Assert.Equal(SnapshotValue, result.Value.Value);
        Assert.Equal(SnapshotNotes, result.Value.Notes);
        Assert.Equal(VaultEnumMap.ToDto(secret.Value.Type), result.Value.Type);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.OneTimeLinkSnapshotRevealed, entry.EventType);
        Assert.Equal(Owner, entry.ActorId);
        Assert.Equal(Owner, entry.OwnerId);
        Assert.Equal(secret.Value.Id, entry.SecretId);
        Assert.Equal(secret.Value.Id.ToString("D"), entry.TargetId);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }

    [Fact]
    public async Task Handle_SecretWithoutNotes_ReturnsNullNotes()
    {
        var secret = GivenSecretWithFields(notes: null);

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Notes);
        Assert.Equal(SnapshotValue, result.Value.Value);
    }

    [Fact]
    public async Task Handle_Disabled_ReturnsForbiddenAndReadsNothing()
    {
        var secret = GivenSecretWithFields();

        var result = await Handler(enabled: false).Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.Forbidden, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_SecretMissingAtTheGate_ReturnsNotFound()
    {
        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, Guid.NewGuid()), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_SecretGoneAfterTheGate_ReturnsNotFoundAndOpensNoKey()
    {
        var secret = SecretWith("Gone");
        Store.GetSecretAsync(Owner, secret.Id, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<Secret>?>(new Versioned<Secret>(secret, NextETag())),
            Task.FromResult<Versioned<Secret>?>(null));

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_RecycledSecret_ReturnsNotFoundWithoutLookingAtRecycledRows()
    {
        var recycled = GivenRecycledSecret(SecretWith("Recycled"));

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, recycled.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await Store.DidNotReceive().GetRecycledSecretAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NoManageRight_ReturnsNotFoundAndReadsNoPlaintext()
    {
        GivenNoShares();
        var secret = GivenSecretWithFields();
        var stranger = new StubCurrentUser();

        var result = await Handler(requester: stranger).Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(SharePermission.View)]
    [InlineData(SharePermission.Edit)]
    public async Task Handle_ViewOrEditShare_ReturnsNotFoundAndReadsNoPlaintext(SharePermission permission)
    {
        var secret = GivenSecretWithFields();
        var recipient = new StubCurrentUser();
        GivenShare(recipient.UserId, secret.Value.Id, permission);

        var result = await Handler(requester: recipient).Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_DirectManageShare_ReturnsTheSnapshotAndAuditsTheRecipient()
    {
        var secret = GivenSecretWithFields();
        var manager = new StubCurrentUser();
        GivenShare(manager.UserId, secret.Value.Id, SharePermission.Manage);

        var result = await Handler(requester: manager).Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(SnapshotValue, result.Value!.Value);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(manager.UserId, entry.ActorId);
        Assert.Equal(Owner, entry.OwnerId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AnotherOwnersIdWithMyOwnSecretId_ReturnsNotFound()
    {
        GivenNoShares();
        var secret = GivenSecretWithFields();
        var otherOwner = Guid.NewGuid();

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(otherOwner, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_UnreadableKeyRing_ReturnsRejectedSecretUnreadable()
    {
        var secret = GivenSecretWithFields();
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(OneTimeLinkMessages.SecretUnreadable, result.Error);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsRejectedSecretUnreadable()
    {
        var secret = GivenSecretWithFields();
        GivenNoKeyRing();

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(OneTimeLinkMessages.SecretUnreadable, result.Error);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_MetadataCannotBeDecrypted_ReturnsRejectedSecretUnreadable()
    {
        var damaged = GivenSecret(DamagedSecret());

        var result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, damaged.Value.Id), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(OneTimeLinkMessages.SecretUnreadable, result.Error);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditFails_ThrowsAndReturnsNoPlaintext()
    {
        var secret = GivenSecretWithFields();
        AuditLog.Failure = new TimeoutException("audit down");
        Result<OneTimeLinkSnapshotDto>? result = null;

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            result = await Handler().Handle(new RevealOneTimeLinkSnapshotCommand(Owner, secret.Value.Id), Token));

        Assert.Null(result);
        Assert.Empty(AuditLog.Entries);
    }
}
