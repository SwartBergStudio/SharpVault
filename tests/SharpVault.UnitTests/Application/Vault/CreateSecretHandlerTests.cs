// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class CreateSecretHandlerTests : VaultHandlerTestBase
{
    private readonly CollectingLogger<CreateSecretCommandHandler> _logger = new();
    private readonly string _name = "SV-NAME-" + Guid.NewGuid().ToString("N");
    private readonly string _username = "SV-USER-" + Guid.NewGuid().ToString("N");
    private readonly string _url = "https://sv-url-" + Guid.NewGuid().ToString("N") + ".test/";
    private readonly string _tag = "SV-TAG-" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _value = "SV-VALUE-" + Guid.NewGuid().ToString("N");
    private readonly string _notes = "SV-NOTES-" + Guid.NewGuid().ToString("N");

    private string[] Markers => [_name, _username, _url, _tag, _value, _notes];

    private CreateSecretCommandHandler Handler() =>
        new(Store, KeyRings, Encryptor, Audit, Current, Time, _logger);

    private CreateSecretCommand Command(Guid? folderId = null) =>
        new(SecretTypeDto.Login, _name, _username, _url, _value, _notes, [_tag], folderId);

    private Secret Created() => LastSubmit.Single(change => change.Kind == VaultChangeKind.AddSecret).Secret!;

    [Fact]
    public async Task Handle_NoFolder_SubmitsAddSecretOnlyAndAudits()
    {
        NextWrite = WrittenWith(ETagOf(5));

        var result = await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(LastSubmit);
        Assert.Equal(VaultChangeKind.AddSecret, change.Kind);
        await Store.DidNotReceive().GetFolderAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretCreated, entry.EventType);
        Assert.Equal(Owner, entry.OwnerId);
        Assert.Equal(Owner, entry.ActorId);
        Assert.Equal(change.Secret!.Id, entry.SecretId);
        Assert.Equal(change.Secret.Id.ToString("D"), entry.TargetId);
        Assert.Equal(ETagOf(5), result.Value!.ConcurrencyToken);
        Assert.Equal(change.Secret.Id, result.Value.Id);
    }

    [Fact]
    public async Task Handle_InFolder_SubmitsTouchFolderThenAddSecret()
    {
        var folder = GivenFolder();
        NextWrite = WrittenWith(ETagOf(5), ETagOf(6));

        var result = await Handler().Handle(Command(folder.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.TouchFolder, VaultChangeKind.AddSecret], LastSubmit.Select(change => change.Kind));
        Assert.Equal(folder.Value.Id, LastSubmit[0].ItemId);
        Assert.Equal(folder.ETag, LastSubmit[0].ETag);
        Assert.Equal(folder.Value.Id, Created().FolderId);
        Assert.Equal(ETagOf(6), result.Value!.ConcurrencyToken);
        Assert.Equal(folder.Value.Id, result.Value.FolderId);
    }

    [Fact]
    public async Task Handle_FolderMissing_ReturnsNotFoundWithoutSubmit()
    {
        var result = await Handler().Handle(Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderOfAnotherOwner_ReturnsNotFoundAndCreatesNothing()
    {
        var foreign = GivenFolder(partition: Guid.NewGuid());

        var result = await Handler().Handle(Command(foreign.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
        await Store.Received().GetFolderAsync(Owner, foreign.Value.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.AlreadyExists, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        NextWrite = Failed(write);

        var result = await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_SubmitConflict_ReturnsConflictWithoutAudit()
    {
        NextWrite = Failed(StoreWriteResult.Conflict);

        var result = await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain(_value, entry.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_EncryptsEveryTextFieldWithItsPurpose()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);
        var secret = Created();

        Assert.Equal(_name, MetadataSession.Decrypt(new EncryptionContext(VaultPurposes.SecretName, secret.Id, Owner), secret.NameEnvelope));
        Assert.Equal(_username, MetadataSession.Decrypt(new EncryptionContext(VaultPurposes.SecretUsername, secret.Id, Owner), secret.UsernameEnvelope!));
        Assert.Equal(_url, MetadataSession.Decrypt(new EncryptionContext(VaultPurposes.SecretUrl, secret.Id, Owner), secret.UrlEnvelope!));
        var tags = MetadataSession.Decrypt(new EncryptionContext(VaultPurposes.SecretTags, secret.Id, Owner), secret.TagsEnvelope!);
        Assert.Equal([_tag], JsonSerializer.Deserialize<string[]>(tags)!);
        using var secretKey = await Harness.OpenSessionAsync(secret.DataKey);
        Assert.Equal(_value, secretKey.Decrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, Owner), secret.ValueEnvelope!));
        Assert.Equal(_notes, secretKey.Decrypt(new EncryptionContext(VaultPurposes.SecretNotes, secret.Id, Owner), secret.NotesEnvelope!));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_EnvelopesAreBoundToPurposeItemAndOwner()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);
        var secret = Created();
        using var secretKey = await Harness.OpenSessionAsync(secret.DataKey);

        Assert.Throws<SecretDecryptionException>(() => secretKey.Decrypt(new EncryptionContext(VaultPurposes.SecretNotes, secret.Id, Owner), secret.ValueEnvelope!));
        Assert.Throws<SecretDecryptionException>(() => secretKey.Decrypt(new EncryptionContext(VaultPurposes.SecretValue, Guid.NewGuid(), Owner), secret.ValueEnvelope!));
        Assert.Throws<SecretDecryptionException>(() => secretKey.Decrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, Guid.NewGuid()), secret.ValueEnvelope!));
    }

    [Fact]
    public async Task Handle_ValueUsesNewPerSecretKeyAndNameUsesMetadataKey()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);
        var first = Created();
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);
        var second = Submitted[^1].Single().Secret!;

        Assert.NotEqual(first.DataKey.Value, second.DataKey.Value);
        Assert.NotEqual(Ring.MetadataKey.Value, first.DataKey.Value);
        Assert.Equal(2, Encryptor.CreateCount);
        Assert.Equal(Ring.MetadataKeyVersion, first.MetadataKeyVersion);
        using var metadataKey = await Harness.OpenSessionAsync(Ring.MetadataKey);
        Assert.Equal(_name, metadataKey.Decrypt(new EncryptionContext(VaultPurposes.SecretName, first.Id, Owner), first.NameEnvelope));
        using var secretKey = await Harness.OpenSessionAsync(first.DataKey);
        Assert.Throws<SecretDecryptionException>(() => secretKey.Decrypt(new EncryptionContext(VaultPurposes.SecretName, first.Id, Owner), first.NameEnvelope));
        Assert.Throws<SecretDecryptionException>(() => metadataKey.Decrypt(new EncryptionContext(VaultPurposes.SecretValue, first.Id, Owner), first.ValueEnvelope!));
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(Command(), TestContext.Current.CancellationToken));

        Assert.Single(Submitted);
    }

    [Fact]
    public async Task Handle_IdsAreVersion7()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        Assert.Equal(7, Created().Id.Version);
    }

    [Fact]
    public async Task Handle_LaterCreateGetsALargerId()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);
        var firstId = Created().Id;
        Time.Advance(TimeSpan.FromMinutes(5));
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);
        var secondId = Submitted[^1].Single().Secret!.Id;

        Assert.True(secondId.CompareTo(firstId) > 0);
    }

    [Fact]
    public async Task Handle_SecretIsOwnedByTheCurrentUser()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        var secret = Created();
        Assert.Equal(Owner, secret.OwnerId);
        Assert.Equal(1, secret.Revision);
        Assert.Equal(Now, secret.CreatedAt);
        Assert.Equal(Now, secret.ModifiedAt);
        Assert.Null(secret.DeletedAt);
        Assert.Null(secret.DeletionBatchId);
        await Store.Received(1).SubmitAsync(Owner, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyNameGiven_StoresNullOptionalEnvelopes()
    {
        var command = new CreateSecretCommand(SecretTypeDto.SecureNote, _name, null, null, null, null, null, null);

        var result = await Handler().Handle(command, TestContext.Current.CancellationToken);

        var secret = Created();
        Assert.Null(secret.UsernameEnvelope);
        Assert.Null(secret.UrlEnvelope);
        Assert.Null(secret.TagsEnvelope);
        Assert.Null(secret.ValueEnvelope);
        Assert.Null(secret.NotesEnvelope);
        Assert.False(result.Value!.HasValue);
        Assert.False(result.Value.HasNotes);
        Assert.Equal(SecretTypeDto.SecureNote, result.Value.Type);
    }

    [Fact]
    public async Task Handle_FirstEverSecret_CreatesTheKeyRing()
    {
        GivenNoKeyRing();
        VaultKeyRing? added = null;
        Store.AddKeyRingAsync(Arg.Do<VaultKeyRing>(ring => added = ring), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(call.Arg<VaultKeyRing>(), RingETag)));

        var result = await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(Owner, added.OwnerId);
        Assert.Equal(_name, result.Value!.Name);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Result_NeverCarriesValueOrNotes()
    {
        var result = await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(_value, json, StringComparison.Ordinal);
        Assert.DoesNotContain(_notes, json, StringComparison.Ordinal);
        Assert.True(result.Value!.HasValue);
        Assert.True(result.Value.HasNotes);
        Assert.Equal(_name, result.Value.Name);
        Assert.Equal([_tag], result.Value.Tags);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditEntryAndLogs_ContainNoPlaintext()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(AuditLog.Entries);
        var text = JsonSerializer.Serialize(entry) + string.Join('\n', _logger.Entries.Select(log => log.Message));
        Assert.All(Markers, marker => Assert.DoesNotContain(marker, text, StringComparison.Ordinal));
        Assert.Null(entry.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_StoredEnvelopes_ContainNoPlaintextBytes()
    {
        await Handler().Handle(Command(), TestContext.Current.CancellationToken);
        var secret = Created();

        var stored = new[] { secret.NameEnvelope, secret.UsernameEnvelope!, secret.UrlEnvelope!, secret.TagsEnvelope!, secret.ValueEnvelope!, secret.NotesEnvelope! };
        foreach (var marker in Markers)
        {
            var needle = System.Text.Encoding.UTF8.GetBytes(marker);
            Assert.All(stored, envelope => Assert.False(envelope.AsSpan().IndexOf(needle) >= 0));
        }
    }

    [Fact]
    public void Command_ToString_ReturnsTypeNameOnly()
    {
        Assert.Equal(nameof(CreateSecretCommand), Command().ToString());
    }
}
