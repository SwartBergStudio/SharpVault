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
using SharpVault.Application.Features.Vault.History.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RevealSecretVersionHandlerTests : VaultHandlerTestBase
{
    private RevealSecretVersionCommandHandler Handler() => new(Store, Encryptor, Policy, Audit);

    private (Versioned<Secret> Secret, SecretVersion Version) Seed(string plaintext = "old-value")
    {
        var secret = GivenSecret();
        var version = NewVersion(secret.Value, plaintext: plaintext);
        GivenVersions(secret.Value.Id, version);
        return (secret, version);
    }

    [Fact]
    public async Task Handle_Existing_DecryptsTheOldValue()
    {
        var (secret, version) = Seed("previous-secret");

        var result = await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("previous-secret", result.Value!.Value);
    }

    [Fact]
    public async Task Handle_VersionMissing_ReturnsNotFoundWithoutAudit()
    {
        var secret = GivenSecret();
        GivenVersions(secret.Value.Id);

        var result = await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, Guid.NewGuid(), RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
        Assert.Equal(0, Encryptor.OpenCount);
    }

    [Fact]
    public async Task Handle_SecretMissing_ReturnsNotFoundWithoutAudit()
    {
        var result = await Handler().Handle(new RevealSecretVersionCommand(Guid.NewGuid(), Guid.NewGuid(), RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_VersionOfAnotherSecret_ReturnsNotFound()
    {
        var secret = GivenSecret();
        var otherSecret = GivenSecret();
        var foreignVersion = NewVersion(otherSecret.Value);
        GivenVersions(otherSecret.Value.Id, foreignVersion);

        var result = await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, foreignVersion.Id, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_UsesVersionIdAsItemId()
    {
        var (secret, version) = Seed("bound-to-version");

        var result = await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("bound-to-version", SecretSession.Decrypt(new EncryptionContext(VaultPurposes.SecretValueHistory, version.Id, Owner), version.ValueEnvelope));
        Assert.Throws<SecretDecryptionException>(() => SecretSession.Decrypt(new EncryptionContext(VaultPurposes.SecretValueHistory, secret.Value.Id, Owner), version.ValueEnvelope));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_VersionEnvelopeEncryptedUnderTheSecretIdContext_ThrowsWithoutAudit()
    {
        var secret = GivenSecret();
        var version = NewVersion(secret.Value);
        version.ValueEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValueHistory, secret.Value.Id, Owner), "wrong-context");
        GivenVersions(secret.Value.Id, version);

        await Assert.ThrowsAsync<SecretDecryptionException>(() =>
            Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken));

        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_CurrentValueEnvelopeStoredAsVersion_ThrowsWithoutAudit()
    {
        var secret = GivenSecret();
        var version = NewVersion(secret.Value);
        version.ValueEnvelope = secret.Value.ValueEnvelope!;
        GivenVersions(secret.Value.Id, version);

        await Assert.ThrowsAsync<SecretDecryptionException>(() =>
            Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken));

        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditsVersionRevealed()
    {
        var (secret, version) = Seed();

        await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken);

        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretVersionRevealed, entry.EventType);
        Assert.Equal(version.Id.ToString("D"), entry.TargetId);
        Assert.Equal(secret.Value.Id, entry.SecretId);
        Assert.Equal(Owner, entry.OwnerId);
        Assert.Equal(Owner, entry.ActorId);
    }

    [Fact]
    public async Task Handle_CopyIntent_AuditsVersionCopied()
    {
        var (secret, version) = Seed();

        await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.Copy), TestContext.Current.CancellationToken);

        Assert.Equal(AuditEventType.SecretVersionCopied, Assert.Single(AuditLog.Entries).EventType);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditThrows_PropagatesAndReturnsNoValue()
    {
        var (secret, version) = Seed("never-leaves");
        AuditLog.Failure = new TimeoutException("audit down");

        Result<RevealedValueDto>? result = null;
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            result = await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken));

        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditNotYetComplete_ValueIsNotReturned()
    {
        var gate = new TaskCompletionSource();
        var gated = new GatedAuditLog(gate.Task);
        var writer = new VaultAuditWriter(Current, new StubRequestMetadata(), gated, Time, NullLogger<VaultAuditWriter>.Instance);
        var handler = new RevealSecretVersionCommandHandler(Store, Encryptor, Policy, writer);
        var (secret, version) = Seed("held-back");

        var pending = handler.Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken);
        await gated.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(pending.IsCompleted);
        gate.SetResult();
        Assert.Equal("held-back", (await pending).Value!.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFoundWithoutStoreCall()
    {
        GivenNoGrant();
        var (secret, version) = Seed();

        var result = await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AsksThePolicyForView()
    {
        var (secret, version) = Seed();

        await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.View), TestContext.Current.CancellationToken);

        Assert.Equal([(secret.Value.Id, SecretPermission.View)], Policy.Requests);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditEntry_ContainsNoPlaintext()
    {
        var marker = "SV-VALUE-" + Guid.NewGuid().ToString("N");
        var (secret, version) = Seed(marker);

        await Handler().Handle(new RevealSecretVersionCommand(secret.Value.Id, version.Id, RevealIntentDto.Copy), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(marker, JsonSerializer.Serialize(AuditLog.Entries), StringComparison.Ordinal);
    }

    private sealed class GatedAuditLog(Task gate) : IAuditLog
    {
        public TaskCompletionSource Started { get; } = new();

        public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await gate.WaitAsync(cancellationToken);
        }
    }
}
