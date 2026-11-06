// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RevealSecretFieldHandlerTests : VaultHandlerTestBase
{
    private RevealSecretFieldCommandHandler Handler() => new(Store, Encryptor, Policy, Audit);

    private Versioned<Secret> SecretWithPlaintext(string value = "the-value", string notes = "the-notes")
    {
        var secret = SecretWith("Revealable");
        secret.ValueEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, Owner), value);
        secret.NotesEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretNotes, secret.Id, Owner), notes);
        return GivenSecret(secret);
    }

    [Fact]
    public async Task Handle_Value_DecryptsAndAuditsRevealed()
    {
        var secret = SecretWithPlaintext("hunter2");

        var result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("hunter2", result.Value!.Value);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretValueRevealed, entry.EventType);
        Assert.Equal(secret.Value.Id, entry.SecretId);
        Assert.Equal(secret.Value.Id.ToString("D"), entry.TargetId);
        Assert.Equal(Owner, entry.OwnerId);
        Assert.Equal(Owner, entry.ActorId);
    }

    [Fact]
    public async Task Handle_CopyIntent_AuditsCopied()
    {
        var secret = SecretWithPlaintext();

        await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.Copy), TestContext.Current.CancellationToken);

        Assert.Equal(AuditEventType.SecretValueCopied, Assert.Single(AuditLog.Entries).EventType);
    }

    [Theory]
    [InlineData(RevealFieldDto.Value, RevealIntentDto.View, AuditEventType.SecretValueRevealed)]
    [InlineData(RevealFieldDto.Value, RevealIntentDto.Copy, AuditEventType.SecretValueCopied)]
    [InlineData(RevealFieldDto.Notes, RevealIntentDto.View, AuditEventType.SecretNotesRevealed)]
    [InlineData(RevealFieldDto.Notes, RevealIntentDto.Copy, AuditEventType.SecretNotesCopied)]
    public async Task Handle_FieldAndIntent_AuditExactlyTheMatchingEvent(RevealFieldDto field, RevealIntentDto intent, AuditEventType expected)
    {
        var secret = SecretWithPlaintext("v-text", "n-text");

        var result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, field, intent), TestContext.Current.CancellationToken);

        Assert.Equal(field == RevealFieldDto.Value ? "v-text" : "n-text", result.Value!.Value);
        Assert.Equal(expected, Assert.Single(AuditLog.Entries).EventType);
    }

    [Fact]
    public async Task Handle_Notes_AuditsNotesEvent()
    {
        var secret = SecretWithPlaintext(notes: "line1\nline2");

        var result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Notes, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal("line1\nline2", result.Value!.Value);
        Assert.Equal(AuditEventType.SecretNotesRevealed, Assert.Single(AuditLog.Entries).EventType);
    }

    [Theory]
    [InlineData(RevealFieldDto.Value)]
    [InlineData(RevealFieldDto.Notes)]
    public async Task Handle_FieldAbsent_ReturnsNotFoundWithoutUnwrap(RevealFieldDto field)
    {
        var plain = SecretWith("Bare");
        plain.ValueEnvelope = null;
        plain.NotesEnvelope = null;
        var secret = GivenSecret(plain);

        var result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, field, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Equal(0, Harness.Provider.UnwrapCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_OnlyNotesAbsent_ValueStillRevealed()
    {
        var plain = SecretWith("Half");
        plain.NotesEnvelope = null;
        var secret = GivenSecret(plain);

        var notes = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Notes, RevealIntentDto.View), TestContext.Current.CancellationToken);
        var value = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, notes.FailureKind);
        Assert.True(value.IsSuccess);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditThrows_PropagatesAndReturnsNoValue()
    {
        var secret = SecretWithPlaintext("never-leaves");
        AuditLog.Failure = new TimeoutException("audit down");

        Result<RevealedValueDto>? result = null;
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken));

        Assert.Null(result);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditNotYetComplete_ValueIsNotReturned()
    {
        var gate = new TaskCompletionSource();
        var gated = new GatedAuditLog(gate.Task);
        var writer = new VaultAuditWriter(Current, new StubRequestMetadata(), gated, Time, NullLogger<VaultAuditWriter>.Instance);
        var handler = new RevealSecretFieldCommandHandler(Store, Encryptor, Policy, writer);
        var secret = SecretWithPlaintext("held-back");

        var pending = handler.Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);
        await gated.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(pending.IsCompleted);
        gate.SetResult();
        var result = await pending;
        Assert.Equal("held-back", result.Value!.Value);
        Assert.Equal(1, gated.Appended);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DecryptionFails_ThrowsSecretDecryptionExceptionWithoutAudit()
    {
        var plain = SecretWith("Tampered");
        var other = SecretWith("Other");
        plain.ValueEnvelope = other.ValueEnvelope;
        var secret = GivenSecret(plain);

        await Assert.ThrowsAsync<SecretDecryptionException>(() =>
            Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken));

        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NotesEnvelopeCopiedIntoValue_ThrowsWithoutAudit()
    {
        var plain = SecretWith("Swapped");
        plain.ValueEnvelope = plain.NotesEnvelope;
        var secret = GivenSecret(plain);

        await Assert.ThrowsAsync<SecretDecryptionException>(() =>
            Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken));

        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFound()
    {
        GivenNoGrant();
        var secret = SecretWithPlaintext();

        var result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal(0, Encryptor.OpenCount);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AsksThePolicyForView()
    {
        var secret = SecretWithPlaintext();

        await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal([(secret.Value.Id, SecretPermission.View)], Policy.Requests);
    }

    [Fact]
    public async Task Handle_Missing_ReturnsNotFoundWithoutAudit()
    {
        var result = await Handler().Handle(new RevealSecretFieldCommand(Guid.NewGuid(), RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecretOfAnotherOwner_ReturnsNotFoundWithoutAudit()
    {
        var secret = GivenSecret(partition: Guid.NewGuid());

        var result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SharedViewer_RevealsFromTheOwnersPartitionAndAuditsTheOwner()
    {
        var sharer = Guid.NewGuid();
        GivenGrant(SecretPermission.View, sharer);
        var plain = SecretWith("Shared");
        var secret = GivenSecret(plain, partition: sharer);

        var result = await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(sharer, entry.OwnerId);
        Assert.Equal(Owner, entry.ActorId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditEntry_ContainsNoPlaintext()
    {
        var marker = "SV-VALUE-" + Guid.NewGuid().ToString("N");
        var secret = SecretWithPlaintext(marker, marker + "-notes");

        await Handler().Handle(new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.Copy), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(marker, JsonSerializer.Serialize(AuditLog.Entries), StringComparison.Ordinal);
        Assert.Null(Assert.Single(AuditLog.Entries).Reason);
    }

    [Fact]
    public async Task Handle_TwoReveals_WriteTwoAuditEntries()
    {
        var secret = SecretWithPlaintext();
        var command = new RevealSecretFieldCommand(secret.Value.Id, RevealFieldDto.Value, RevealIntentDto.View);

        await Handler().Handle(command, TestContext.Current.CancellationToken);
        await Handler().Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, AuditLog.Entries.Count);
    }

    private sealed class GatedAuditLog(Task gate) : IAuditLog
    {
        public TaskCompletionSource Started { get; } = new();

        public int Appended { get; private set; }

        public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await gate.WaitAsync(cancellationToken);
            Appended++;
        }
    }
}
