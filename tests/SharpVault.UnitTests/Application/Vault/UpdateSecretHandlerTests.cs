// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class UpdateSecretHandlerTests : VaultHandlerTestBase
{
    private readonly CollectingLogger<UpdateSecretCommandHandler> _logger = new();

    private UpdateSecretCommandHandler Handler() => new(Store, KeyRings, Encryptor, Policy, Audit, Time, _logger);

    private static UpdateSecretCommand Command(Versioned<Secret> secret, string? etag = null, bool replaceValue = false, string? value = null, bool replaceNotes = false, string? notes = null) =>
        new(secret.Value.Id, etag ?? secret.ETag, SecretTypeDto.ApiKey, "Renamed", "new-user", "https://new.test/", ["x"], replaceValue, value, replaceNotes, notes);

    private Versioned<Secret> Existing()
    {
        var secret = SecretWith("Old name", "old-user", "https://old.test/", "old");
        secret.Revision = 4;
        return GivenSecret(secret);
    }

    private SecretVersion[] VersionsOf(Secret secret, int count) =>
        [.. Enumerable.Range(0, count).Select(i => NewVersion(secret, Now.AddDays(-(count - i)), "old-" + i))];

    [Fact]
    public async Task Handle_StaleETag_ReturnsConflictWithoutSubmit()
    {
        var secret = Existing();

        var result = await Handler().Handle(Command(secret, ETagOf(1)), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DamagedMetadata_RejectsWithoutSubmitOrAudit()
    {
        var secret = GivenSecret(DamagedSecret());

        var result = await Handler().Handle(Command(secret), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.DamagedSecretNotEditable, result.Error);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DamagedMetadataWithReplaceValue_RejectsWithoutOpeningTheSecretKey()
    {
        var secret = GivenSecret(DamagedSecret());

        var result = await Handler().Handle(Command(secret, replaceValue: true, value: "new"), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.DoesNotContain(Encryptor.OpenedKeys, key => key.Value.SequenceEqual(secret.Value.DataKey.Value));
    }

    [Fact]
    public async Task Handle_MetadataOnly_DoesNotOpenSecretKey()
    {
        var secret = Existing();

        var result = await Handler().Handle(Command(secret), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(Encryptor.OpenedKeys, key => key.Value.SequenceEqual(secret.Value.DataKey.Value));
        Assert.Equal(0, Encryptor.CreateCount);
        await Store.DidNotReceive().ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        var change = Assert.Single(LastSubmit);
        Assert.Equal(VaultChangeKind.ReplaceSecret, change.Kind);
        Assert.Equal(secret.ETag, change.ETag);
    }

    [Fact]
    public async Task Handle_MetadataOnly_ReplacesMetadataTypeAndLeavesValueFolderAndNotesAlone()
    {
        var plain = SecretWith("Old name");
        plain.FolderId = Guid.NewGuid();
        var valueBefore = plain.ValueEnvelope;
        var notesBefore = plain.NotesEnvelope;
        var secret = GivenSecret(plain);

        var result = await Handler().Handle(Command(secret), TestContext.Current.CancellationToken);

        var dto = result.Value!;
        Assert.Equal("Renamed", dto.Name);
        Assert.Equal("new-user", dto.Username);
        Assert.Equal("https://new.test/", dto.Url);
        Assert.Equal(["x"], dto.Tags);
        Assert.Equal(SecretTypeDto.ApiKey, dto.Type);
        Assert.Equal(plain.FolderId, dto.FolderId);
        var saved = LastSubmit[0].Secret!;
        Assert.Same(valueBefore, saved.ValueEnvelope);
        Assert.Same(notesBefore, saved.NotesEnvelope);
        Assert.Equal(Now, saved.ModifiedAt);
    }

    [Fact]
    public async Task Handle_ReplaceValue_AddsVersionReEncryptedUnderVersionId()
    {
        var existing = Existing();
        var oldValue = DecryptCurrentValue(existing.Value);
        var oldRevision = existing.Value.Revision;
        GivenVersions(existing.Value.Id);

        var result = await Handler().Handle(Command(existing, replaceValue: true, value: "brand-new"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.ReplaceSecret, VaultChangeKind.AddVersion], LastSubmit.Select(change => change.Kind));
        var version = LastSubmit[1].Version!;
        Assert.Equal(7, version.Id.Version);
        Assert.Equal(existing.Value.Id, version.SecretId);
        Assert.Equal(Owner, version.OwnerId);
        Assert.Equal(oldRevision, version.Revision);
        Assert.Equal(Now, version.ReplacedAt);
        Assert.Equal(oldValue, DecryptVersionValue(version));
        Assert.Equal("brand-new", DecryptCurrentValue(LastSubmit[0].Secret!));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ReplaceValue_HistoryEnvelopeCannotBeReadAsTheCurrentValue()
    {
        var existing = Existing();
        GivenVersions(existing.Value.Id);

        await Handler().Handle(Command(existing, replaceValue: true, value: "brand-new"), TestContext.Current.CancellationToken);

        var version = LastSubmit[1].Version!;
        var asCurrent = new Secret
        {
            Id = version.SecretId,
            OwnerId = Owner,
            DataKey = existing.Value.DataKey,
            ValueEnvelope = version.ValueEnvelope,
        };
        Assert.Throws<SecretDecryptionException>(() => DecryptCurrentValue(asCurrent));
    }

    [Fact]
    public async Task Handle_ReplaceValueWithoutOldValue_AddsNoVersion()
    {
        var plain = SecretWith("No value");
        plain.ValueEnvelope = null;
        var existing = GivenSecret(plain);

        var result = await Handler().Handle(Command(existing, replaceValue: true, value: "first"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.ReplaceSecret], LastSubmit.Select(change => change.Kind));
        Assert.Equal("first", DecryptCurrentValue(LastSubmit[0].Secret!));
        await Store.DidNotReceive().ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplaceValueWithNull_ArchivesAndClearsTheValue()
    {
        var existing = Existing();
        GivenVersions(existing.Value.Id);

        var result = await Handler().Handle(Command(existing, replaceValue: true, value: null), TestContext.Current.CancellationToken);

        Assert.False(result.Value!.HasValue);
        Assert.Null(LastSubmit[0].Secret!.ValueEnvelope);
        Assert.Contains(LastSubmit, change => change.Kind == VaultChangeKind.AddVersion);
    }

    [Fact]
    public async Task Handle_ReplaceValueAt20Versions_DeletesOldest()
    {
        var existing = Existing();
        var versions = VersionsOf(existing.Value, VaultLimits.HistoryVersionLimit);
        var listed = GivenVersions(existing.Value.Id, [.. versions.Reverse()]);

        await Handler().Handle(Command(existing, replaceValue: true, value: "new"), TestContext.Current.CancellationToken);

        var deletes = LastSubmit.Where(change => change.Kind == VaultChangeKind.DeleteVersion).ToList();
        var oldest = listed.OrderBy(version => version.Value.ReplacedAt).First();
        var removed = Assert.Single(deletes);
        Assert.Equal(oldest.Value.Id, removed.Version!.Id);
        Assert.Equal(oldest.ETag, removed.ETag);
        Assert.Single(LastSubmit, change => change.Kind == VaultChangeKind.AddVersion);
    }

    [Fact]
    public async Task Handle_ReplaceValueAt19Versions_DeletesNothing()
    {
        var existing = Existing();
        GivenVersions(existing.Value.Id, VersionsOf(existing.Value, VaultLimits.HistoryVersionLimit - 1));

        await Handler().Handle(Command(existing, replaceValue: true, value: "new"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(LastSubmit, change => change.Kind == VaultChangeKind.DeleteVersion);
    }

    [Fact]
    public async Task Handle_ReplaceValueAt25Versions_DeletesTheSixOldest()
    {
        var existing = Existing();
        var listed = GivenVersions(existing.Value.Id, VersionsOf(existing.Value, 25));

        await Handler().Handle(Command(existing, replaceValue: true, value: "new"), TestContext.Current.CancellationToken);

        var expected = listed.OrderBy(version => version.Value.ReplacedAt).Take(6).Select(version => version.Value.Id).Order();
        var deleted = LastSubmit.Where(change => change.Kind == VaultChangeKind.DeleteVersion).Select(change => change.Version!.Id).Order();
        Assert.Equal(expected, deleted);
    }

    [Fact]
    public async Task Handle_ReplaceNotesOnly_AddsNoVersionAndKeepsTheValue()
    {
        var existing = Existing();
        var valueBefore = existing.Value.ValueEnvelope;

        var result = await Handler().Handle(Command(existing, replaceNotes: true, notes: "fresh notes"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.ReplaceSecret], LastSubmit.Select(change => change.Kind));
        Assert.Same(valueBefore, LastSubmit[0].Secret!.ValueEnvelope);
        Assert.True(result.Value!.HasNotes);
    }

    [Fact]
    public async Task Handle_ReplaceNotesNull_ClearsNotes()
    {
        var existing = Existing();

        var result = await Handler().Handle(Command(existing, replaceNotes: true, notes: null), TestContext.Current.CancellationToken);

        Assert.False(result.Value!.HasNotes);
        Assert.Null(LastSubmit[0].Secret!.NotesEnvelope);
    }

    [Fact]
    public async Task Handle_RevisionIncrements()
    {
        var existing = Existing();

        var result = await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, LastSubmit[0].Secret!.Revision);
    }

    [Fact]
    public async Task Handle_Success_AuditsSecretUpdatedAndReturnsTheNewRowETag()
    {
        var existing = Existing();
        NextWrite = WrittenWith(ETagOf(9));

        var result = await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.Equal(ETagOf(9), result.Value!.ConcurrencyToken);
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretUpdated, entry.EventType);
        Assert.Equal(existing.Value.Id, entry.SecretId);
        Assert.Equal(existing.Value.Id.ToString("D"), entry.TargetId);
        Assert.Equal(Owner, entry.OwnerId);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var existing = Existing();
        NextWrite = Failed(write);

        var result = await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var existing = Existing();
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(Command(existing), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_GrantView_ReturnsNotFound()
    {
        GivenGrant(SecretPermission.View, Owner);
        var existing = Existing();

        var result = await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.DoesNotContain(Store.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IVaultStore.GetSecretAsync));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFoundWithoutStoreCall()
    {
        GivenNoGrant();
        var existing = Existing();

        var result = await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AsksThePolicyForEdit()
    {
        var existing = Existing();

        await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.Equal([(existing.Value.Id, SecretPermission.Edit)], Policy.Requests);
    }

    [Fact]
    public async Task Handle_SecretMissing_ReturnsNotFound()
    {
        var existing = new Versioned<Secret>(SecretWith("ghost"), ETagOf(3));

        var result = await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        var existing = Existing();

        var result = await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_WritesToTheGrantedOwnersPartition()
    {
        var other = Guid.NewGuid();
        GivenGrant(SecretPermission.Edit, other);
        var existing = GivenSecret(partition: other);

        await Handler().Handle(Command(existing), TestContext.Current.CancellationToken);

        await Store.DidNotReceive().SubmitAsync(Owner, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
        await Store.DidNotReceive().GetSecretAsync(Owner, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ResultAuditAndLogs_NeverContainTheNewValue()
    {
        var existing = Existing();
        GivenVersions(existing.Value.Id);
        var marker = "SV-VALUE-" + Guid.NewGuid().ToString("N");

        var result = await Handler().Handle(Command(existing, replaceValue: true, value: marker, replaceNotes: true, notes: marker + "n"), TestContext.Current.CancellationToken);

        var text = JsonSerializer.Serialize(result.Value) + JsonSerializer.Serialize(AuditLog.Entries) + string.Join('\n', _logger.Entries.Select(log => log.Message));
        Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_ToString_ReturnsTypeNameOnly()
    {
        var existing = new Versioned<Secret>(SecretWith("x"), ETagOf(1));

        Assert.Equal(nameof(UpdateSecretCommand), Command(existing, replaceValue: true, value: "secret").ToString());
    }
}
