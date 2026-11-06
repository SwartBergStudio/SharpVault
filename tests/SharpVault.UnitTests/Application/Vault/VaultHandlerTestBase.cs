// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.ExceptionServices;
using System.Globalization;
using AutoFixture;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

[Collection(MapsterGlobalSettings.Name)]
public abstract class VaultHandlerTestBase : IAsyncLifetime
{
    protected static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly object _submittedGate = new();

    private int _etagCounter;

    protected VaultHandlerTestBase()
    {
        GlobalMapping.EnsureApplicationMappings();
        Owner = Guid.NewGuid();
        Current = new StubCurrentUser(Owner);
        Time = new FakeTimeProvider(Now);
        Audit = new VaultAuditWriter(Current, new StubRequestMetadata(), AuditLog, Time, NullLogger<VaultAuditWriter>.Instance);
        Encryptor = new TrackingEncryptor(Harness.Encryptor);
        KeyRings = new VaultKeyRingService(Store, Encryptor, Time, NullLogger<VaultKeyRingService>.Instance);
    }

    protected Guid Owner { get; }

    private protected EncryptionHarness Harness { get; } = new();

    protected TrackingEncryptor Encryptor { get; }

    protected IVaultStore Store { get; } = Substitute.For<IVaultStore>();

    private protected StubSecretAccessPolicy Policy { get; } = new();

    private protected RecordingAuditLog AuditLog { get; } = new();

    protected FakeTimeProvider Time { get; }

    private protected StubCurrentUser Current { get; }

    protected VaultAuditWriter Audit { get; }

    protected VaultKeyRingService KeyRings { get; }

    protected IFixture Fixture { get; } = new Fixture();

    protected IDataKeySession MetadataSession { get; private set; } = null!;

    protected IDataKeySession SecretSession { get; private set; } = null!;

    protected VaultKeyRing Ring { get; private set; } = null!;

    protected string RingETag { get; } = ETagOf(0);

    protected List<IReadOnlyList<VaultChange>> Submitted { get; } = [];

    protected VaultWriteResult? NextWrite { get; set; }

    protected IReadOnlyList<VaultChange> LastSubmit => Submitted[^1];

    public async ValueTask InitializeAsync()
    {
        MetadataSession = await Harness.CreateSessionAsync();
        SecretSession = await Harness.CreateSessionAsync();
        Ring = new VaultKeyRing
        {
            OwnerId = Owner,
            MetadataKey = MetadataSession.WrappedKey,
            MetadataKeyVersion = VaultLimits.CurrentMetadataKeyVersion,
            CreatedAt = VaultSamples.Created,
        };
        Fixture.Customize(MetadataSession, SecretSession, Owner);
        GivenKeyRing();
        GivenOwnerGrant();
        GivenSubmitWrites();
    }

    public ValueTask DisposeAsync()
    {
        MetadataSession.Dispose();
        SecretSession.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected static string ETagOf(int number) =>
        string.Create(CultureInfo.InvariantCulture, $"W/\"datetime'2026-03-01T09%3A00%3A{number:D2}.0000000Z'\"");

    protected string NextETag() => ETagOf(Interlocked.Increment(ref _etagCounter) + 10);

    protected void GivenKeyRing() =>
        Store.GetKeyRingAsync(Owner, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(Ring, RingETag)));

    private protected void GivenUnreadableKeyRing() =>
        Harness.Provider.UnwrapFailure = () => new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);

    protected void GivenNoKeyRing() =>
        Store.GetKeyRingAsync(Owner, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));

    protected void GivenOwnerGrant() => GivenGrant(SecretPermission.Owner, Owner);

    protected void GivenGrant(SecretPermission granted, Guid owner) => Policy.Grant = new SecretAccessGrant(owner, granted);

    protected void GivenNoGrant() => Policy.Grant = null;

    protected Versioned<Secret> GivenSecret(Secret? secret = null, string? etag = null, Guid? partition = null)
    {
        var versioned = new Versioned<Secret>(secret ?? Fixture.Create<Secret>(), etag ?? NextETag());
        Store.GetSecretAsync(partition ?? Owner, versioned.Value.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Secret>?>(versioned));
        return versioned;
    }

    protected Versioned<Secret> GivenRecycledSecret(Secret? secret = null, string? etag = null)
    {
        var plain = secret ?? Fixture.Create<Secret>();
        plain.DeletedAt = Now.AddDays(-1);
        plain.DeletionBatchId = Guid.NewGuid();
        var versioned = new Versioned<Secret>(plain, etag ?? NextETag());
        Store.GetRecycledSecretAsync(Owner, plain.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Secret>?>(versioned));
        return versioned;
    }

    protected Versioned<Folder> GivenFolder(Folder? folder = null, string? etag = null, Guid? partition = null)
    {
        var versioned = new Versioned<Folder>(folder ?? Fixture.Create<Folder>(), etag ?? NextETag());
        Store.GetFolderAsync(partition ?? Owner, versioned.Value.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Folder>?>(versioned));
        return versioned;
    }

    protected IReadOnlyList<Versioned<Folder>> GivenFolders(params Folder[] folders)
    {
        var list = folders.Select(folder => new Versioned<Folder>(folder, NextETag())).ToList();
        Store.ListFoldersAsync(Owner, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<Folder>>>(list));
        foreach (var versioned in list)
        {
            Store.GetFolderAsync(Owner, versioned.Value.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Versioned<Folder>?>(versioned));
        }

        return list;
    }

    protected Secret SecretWith(string name, string? username = null, string? url = null, params string[] tags)
    {
        var secret = Fixture.Create<Secret>();
        SecretMetadataCipher.Encrypt(MetadataSession, secret, new SecretMetadata(name, username, url, tags));
        return secret;
    }

    protected Secret DamagedSecret()
    {
        var secret = Fixture.Create<Secret>();
        (secret.NameEnvelope, secret.UsernameEnvelope) = (secret.UsernameEnvelope!, secret.NameEnvelope);
        return secret;
    }

    protected Folder RecycledFolderIn(Guid batch, Guid? parentId = null)
    {
        var folder = NewFolder(parentId);
        folder.DeletedAt = Now.AddDays(-1);
        folder.DeletionBatchId = batch;
        return folder;
    }

    protected Secret RecycledSecretIn(Guid batch, Guid? folderId)
    {
        var secret = Fixture.Create<Secret>();
        secret.FolderId = folderId;
        secret.DeletedAt = Now.AddDays(-1);
        secret.DeletionBatchId = batch;
        return secret;
    }

    protected RecycledBatch GivenBatch(Guid batch, IReadOnlyList<Folder> folders, IReadOnlyList<Secret> secrets)
    {
        var recycled = new RecycledBatch(
            [.. secrets.Select(secret => new Versioned<Secret>(secret, NextETag()))],
            [.. folders.Select(folder => new Versioned<Folder>(folder, NextETag()))]);
        Store.ListRecycledBatchAsync(Owner, batch, Arg.Any<CancellationToken>()).Returns(Task.FromResult(recycled));
        foreach (var folder in recycled.Folders)
        {
            Store.GetRecycledFolderAsync(Owner, folder.Value.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(folder));
        }

        foreach (var secret in recycled.Secrets)
        {
            Store.GetRecycledSecretAsync(Owner, secret.Value.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Secret>?>(secret));
        }

        return recycled;
    }

    protected Folder NewFolder(Guid? parentId = null)
    {
        var folder = Fixture.Create<Folder>();
        folder.ParentId = parentId;
        return folder;
    }

    protected IReadOnlyList<Versioned<SecretVersion>> GivenVersions(Guid secretId, params SecretVersion[] versions)
    {
        var list = versions.Select(version => new Versioned<SecretVersion>(version, NextETag())).ToList();
        Store.ListVersionsAsync(Owner, secretId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<SecretVersion>>>(list));
        foreach (var versioned in list)
        {
            Store.GetVersionAsync(Owner, secretId, versioned.Value.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Versioned<SecretVersion>?>(versioned));
        }

        return list;
    }

    protected SecretVersion NewVersion(Secret secret, DateTimeOffset? replacedAt = null, string plaintext = "old")
    {
        var id = Guid.CreateVersion7();
        return new SecretVersion
        {
            Id = id,
            SecretId = secret.Id,
            OwnerId = secret.OwnerId,
            Revision = secret.Revision,
            ReplacedAt = replacedAt ?? Now.AddDays(-1),
            ValueEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValueHistory, id, secret.OwnerId), plaintext),
        };
    }

    protected string DecryptCurrentValue(Secret secret) =>
        SecretSession.Decrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, secret.OwnerId), secret.ValueEnvelope!);

    protected string DecryptVersionValue(SecretVersion version) =>
        SecretSession.Decrypt(new EncryptionContext(VaultPurposes.SecretValueHistory, version.Id, version.OwnerId), version.ValueEnvelope);

    protected void GivenSubmitWrites() =>
        Store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                Record(changes);
                if (NextWrite is { } forced)
                {
                    return Task.FromResult(forced);
                }

                var etags = changes.Select(_ => (string?)NextETag()).ToList();
                return Task.FromResult(new VaultWriteResult(StoreWriteResult.Written, changes.Count, etags));
            });

    protected void GivenSubmitScript(Func<IReadOnlyList<VaultChange>, VaultWriteResult?> script) =>
        Store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                Record(changes);
                var scripted = script(changes);
                if (scripted is not null)
                {
                    return Task.FromResult(scripted);
                }

                return Task.FromResult(new VaultWriteResult(StoreWriteResult.Written, changes.Count, [.. changes.Select(_ => (string?)NextETag())]));
            });

    protected VaultWriteResult FailedAfter(StoreWriteResult result, int completedChanges, IReadOnlyList<VaultChange> changes) =>
        new(result, completedChanges, [.. changes.Select((_, index) => index < completedChanges ? NextETag() : null)]);

    protected VaultWriteResult Interrupted(Exception exception, int completedChanges, IReadOnlyList<VaultChange> changes) =>
        new(StoreWriteResult.Conflict, completedChanges, [.. changes.Select((_, index) => index < completedChanges ? NextETag() : null)], ExceptionDispatchInfo.Capture(exception));

    protected void Record(IReadOnlyList<VaultChange> changes)
    {
        lock (_submittedGate)
        {
            Submitted.Add(changes);
        }
    }

    protected static VaultWriteResult WrittenWith(params string[] etags) => new(StoreWriteResult.Written, etags.Length, etags);

    protected static VaultWriteResult Failed(StoreWriteResult result) => new(result, 0, []);

    protected sealed class TrackingEncryptor(IEnvelopeEncryptor inner) : IEnvelopeEncryptor
    {
        public int CreateCount { get; private set; }

        public int OpenCount { get; private set; }

        public List<WrappedDataKey> OpenedKeys { get; } = [];

        public List<WrappedDataKey> CreatedKeys { get; } = [];

        public async Task<IDataKeySession> CreateDataKeyAsync(CancellationToken cancellationToken)
        {
            CreateCount++;
            var session = await inner.CreateDataKeyAsync(cancellationToken);
            CreatedKeys.Add(session.WrappedKey);
            return session;
        }

        public async Task<IDataKeySession> OpenDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
        {
            OpenCount++;
            OpenedKeys.Add(wrappedKey);
            return await inner.OpenDataKeyAsync(wrappedKey, cancellationToken);
        }

        public Task<WrappedDataKey> RewrapDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken) =>
            inner.RewrapDataKeyAsync(wrappedKey, cancellationToken);
    }
}
