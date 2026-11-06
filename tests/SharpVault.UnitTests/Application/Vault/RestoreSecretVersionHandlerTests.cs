// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.History.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RestoreSecretVersionHandlerTests : VaultHandlerTestBase
{
    private RestoreSecretVersionCommandHandler Handler() =>
        new(Store, KeyRings, Encryptor, Policy, Audit, Time, NullLogger<RestoreSecretVersionCommandHandler>.Instance);

    private Versioned<Secret> SecretWithCurrent(string current, long revision = 5)
    {
        var secret = SecretWith("Restorable");
        secret.Revision = revision;
        secret.ValueEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, Owner), current);
        return GivenSecret(secret);
    }

    private SecretVersion[] History(Secret secret, int count) =>
        [.. Enumerable.Range(0, count).Select(i => NewVersion(secret, Now.AddDays(-(count - i)), "v" + i))];

    private static RestoreSecretVersionCommand Restore(Versioned<Secret> secret, SecretVersion version, string? etag = null) =>
        new(secret.Value.Id, version.Id, etag ?? secret.ETag);

    [Fact]
    public async Task Handle_CurrentBecomesNewVersionAndVersionBecomesCurrent()
    {
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value, plaintext: "older-value");
        GivenVersions(secret.Value.Id, target);

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.ReplaceSecret, VaultChangeKind.AddVersion], LastSubmit.Select(change => change.Kind));
        Assert.Equal(secret.ETag, LastSubmit[0].ETag);
        Assert.Equal("older-value", DecryptCurrentValue(LastSubmit[0].Secret!));
        var archived = LastSubmit[1].Version!;
        Assert.Equal("current-value", DecryptVersionValue(archived));
        Assert.NotEqual(target.Id, archived.Id);
        Assert.Equal(7, archived.Id.Version);
        Assert.Equal(secret.Value.Id, archived.SecretId);
        Assert.Equal(Owner, archived.OwnerId);
        Assert.Equal(5, archived.Revision);
        Assert.Equal(Now, archived.ReplacedAt);
    }

    [Fact]
    public async Task Handle_RestoredVersionStaysInHistoryUntouched()
    {
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value, plaintext: "older-value");
        GivenVersions(secret.Value.Id, target);

        await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(LastSubmit, change => change.Version?.Id == target.Id);
        Assert.Equal("older-value", DecryptVersionValue(target));
    }

    [Fact]
    public async Task Handle_NoCurrentValue_AddsNoNewVersion()
    {
        var plain = SecretWith("Empty now");
        plain.ValueEnvelope = null;
        var secret = GivenSecret(plain);
        var target = NewVersion(secret.Value, plaintext: "older-value");
        GivenVersions(secret.Value.Id, [.. History(secret.Value, 19), target]);

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([VaultChangeKind.ReplaceSecret], LastSubmit.Select(change => change.Kind));
        Assert.Equal("older-value", DecryptCurrentValue(LastSubmit[0].Secret!));
        await Store.DidNotReceive().ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeepsRestoredVersion()
    {
        var secret = SecretWithCurrent("current-value");
        var versions = History(secret.Value, VaultLimits.HistoryVersionLimit);
        var listed = GivenVersions(secret.Value.Id, versions);
        var oldest = listed.OrderBy(version => version.Value.ReplacedAt).First();
        var secondOldest = listed.OrderBy(version => version.Value.ReplacedAt).Skip(1).First();

        var result = await Handler().Handle(Restore(secret, oldest.Value), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var deleted = Assert.Single(LastSubmit, change => change.Kind == VaultChangeKind.DeleteVersion);
        Assert.NotEqual(oldest.Value.Id, deleted.Version!.Id);
        Assert.Equal(secondOldest.Value.Id, deleted.Version.Id);
        Assert.Equal(secondOldest.ETag, deleted.ETag);
    }

    [Fact]
    public async Task Handle_PrunesTo20()
    {
        var secret = SecretWithCurrent("current-value");
        var versions = History(secret.Value, 25);
        var listed = GivenVersions(secret.Value.Id, versions);
        var newest = listed.OrderByDescending(version => version.Value.ReplacedAt).First();

        await Handler().Handle(Restore(secret, newest.Value), TestContext.Current.CancellationToken);

        var added = LastSubmit.Count(change => change.Kind == VaultChangeKind.AddVersion);
        var deleted = LastSubmit.Where(change => change.Kind == VaultChangeKind.DeleteVersion).Select(change => change.Version!.Id).ToHashSet();
        Assert.Equal(1, added);
        Assert.Equal(25 + added - VaultLimits.HistoryVersionLimit, deleted.Count);
        var expected = listed.OrderBy(version => version.Value.ReplacedAt).Take(deleted.Count).Select(version => version.Value.Id);
        Assert.Equal(expected.Order(), deleted.Order());
        Assert.DoesNotContain(newest.Value.Id, deleted);
    }

    [Fact]
    public async Task Handle_BelowTheLimit_DeletesNothing()
    {
        var secret = SecretWithCurrent("current-value");
        var versions = History(secret.Value, 5);
        GivenVersions(secret.Value.Id, versions);

        await Handler().Handle(Restore(secret, versions[2]), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(LastSubmit, change => change.Kind == VaultChangeKind.DeleteVersion);
    }

    [Fact]
    public async Task Handle_StaleETag_ReturnsConflict()
    {
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);

        var result = await Handler().Handle(Restore(secret, target, ETagOf(1)), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
        Assert.Equal(0, Encryptor.OpenedKeys.Count(key => key.Value.SequenceEqual(secret.Value.DataKey.Value)));
    }

    [Fact]
    public async Task Handle_Restore_BumpsRevisionAndModifiedAt()
    {
        var secret = SecretWithCurrent("current-value", revision: 8);
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);

        await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.Equal(9, LastSubmit[0].Secret!.Revision);
        Assert.Equal(Now, LastSubmit[0].Secret!.ModifiedAt);
    }

    [Fact]
    public async Task Handle_Success_AuditsWithVersionIdAsTargetAndSecretIdAsSecret()
    {
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);
        NextWrite = WrittenWith(ETagOf(12), ETagOf(13));

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.SecretVersionRestored, entry.EventType);
        Assert.Equal(target.Id.ToString("D"), entry.TargetId);
        Assert.Equal(secret.Value.Id, entry.SecretId);
        Assert.Equal(Owner, entry.OwnerId);
        Assert.Equal(ETagOf(12), result.Value!.ConcurrencyToken);
    }

    [Fact]
    public async Task Handle_VersionMissing_ReturnsNotFound()
    {
        var secret = SecretWithCurrent("current-value");
        GivenVersions(secret.Value.Id);

        var result = await Handler().Handle(new RestoreSecretVersionCommand(secret.Value.Id, Guid.NewGuid(), secret.ETag), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_VersionOfAnotherSecret_ReturnsNotFoundAndChangesNothing()
    {
        var secret = SecretWithCurrent("current-value");
        var other = GivenSecret();
        var foreign = NewVersion(other.Value);
        GivenVersions(other.Value.Id, foreign);

        var result = await Handler().Handle(Restore(secret, foreign), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_SecretMissing_ReturnsNotFound()
    {
        var result = await Handler().Handle(new RestoreSecretVersionCommand(Guid.NewGuid(), Guid.NewGuid(), ETagOf(1)), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_TamperedVersionEnvelope_ThrowsWithoutWriteOrAudit()
    {
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        target.ValueEnvelope = secret.Value.ValueEnvelope!;
        GivenVersions(secret.Value.Id, target);

        await Assert.ThrowsAsync<SecretDecryptionException>(() => Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken));

        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);
        NextWrite = Failed(write);

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantView_ReturnsNotFoundWithoutStoreCall()
    {
        GivenGrant(SecretPermission.View, Owner);
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Equal([(secret.Value.Id, SecretPermission.Edit)], Policy.Requests);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFoundWithoutStoreCall()
    {
        GivenNoGrant();
        var secret = SecretWithCurrent("current-value");
        var target = NewVersion(secret.Value);
        GivenVersions(secret.Value.Id, target);

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Result_NeverCarriesTheRestoredValue()
    {
        var secret = SecretWithCurrent("current-value");
        var marker = "SV-VALUE-" + Guid.NewGuid().ToString("N");
        var target = NewVersion(secret.Value, plaintext: marker);
        GivenVersions(secret.Value.Id, target);

        var result = await Handler().Handle(Restore(secret, target), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(marker, System.Text.Json.JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
        Assert.DoesNotContain(marker, System.Text.Json.JsonSerializer.Serialize(AuditLog.Entries), StringComparison.Ordinal);
    }
}
