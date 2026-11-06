// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class VaultKeyRingServiceTests
{
    private const string ETagOne = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";
    private const string ETagTwo = "W/\"datetime'2026-03-01T09%3A00%3A01.0000000Z'\"";

    private readonly EncryptionHarness _harness = new();
    private readonly IVaultStore _store = Substitute.For<IVaultStore>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid();
    private readonly TrackingEncryptor _encryptor;
    private readonly VaultKeyRingService _service;

    public VaultKeyRingServiceTests()
    {
        _encryptor = new TrackingEncryptor(_harness.Encryptor);
        _service = new VaultKeyRingService(_store, _encryptor, _time, NullLogger<VaultKeyRingService>.Instance);
    }

    private async Task<VaultKeyRing> ExistingRingAsync()
    {
        using var session = await _harness.CreateSessionAsync();
        return new VaultKeyRing { OwnerId = _owner, MetadataKey = session.WrappedKey, MetadataKeyVersion = 1, CreatedAt = _time.GetUtcNow() };
    }

    [Fact]
    public async Task OpenAsync_NoRing_ReturnsNullWithoutUnwrap()
    {
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));

        var opened = await _service.OpenAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Null(opened);
        Assert.Equal(0, _harness.Provider.UnwrapCount);
        Assert.Empty(_encryptor.Sessions);
    }

    [Fact]
    public async Task OpenAsync_Ring_UnwrapsOnce()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        var baseline = _harness.Provider.UnwrapCount;

        using var opened = await _service.OpenAsync(_owner, TestContext.Current.CancellationToken);

        Assert.NotNull(opened);
        Assert.Equal(baseline + 1, _harness.Provider.UnwrapCount);
        Assert.Equal(1, opened.MetadataKeyVersion);
        Assert.Equal(ETagOne, opened.KeyRingETag);
    }

    [Fact]
    public async Task OpenAsync_OpenedVault_CanEncryptAndDecryptMetadata()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        using var opened = await _service.OpenAsync(_owner, TestContext.Current.CancellationToken);
        var context = new EncryptionContext(VaultPurposes.FolderName, Guid.NewGuid(), _owner);

        var envelope = opened!.Session.Encrypt(context, "Finance");

        Assert.Equal("Finance", opened.Session.Decrypt(context, envelope));
    }

    [Fact]
    public async Task OpenOrCreateAsync_NoRing_CreatesAndAddsOnce()
    {
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));
        VaultKeyRing? added = null;
        _store.AddKeyRingAsync(Arg.Do<VaultKeyRing>(ring => added = ring), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(call.Arg<VaultKeyRing>(), ETagOne)));

        using var opened = await _service.OpenOrCreateAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Equal(1, _harness.Provider.WrapCount);
        await _store.Received(1).AddKeyRingAsync(Arg.Any<VaultKeyRing>(), Arg.Any<CancellationToken>());
        Assert.NotNull(added);
        Assert.Equal(_owner, added.OwnerId);
        Assert.Equal(VaultLimits.CurrentMetadataKeyVersion, added.MetadataKeyVersion);
        Assert.Equal(_time.GetUtcNow(), added.CreatedAt);
        Assert.Equal(ETagOne, opened.KeyRingETag);
        Assert.Equal(added.MetadataKeyVersion, opened.MetadataKeyVersion);
        Assert.Equal(added.MetadataKey.Value, opened.Session.WrappedKey.Value);
        Assert.False(_encryptor.Sessions.Single().Disposed);
    }

    [Fact]
    public async Task OpenOrCreateAsync_AddReturnsNull_DisposesOwnKeyAndOpensWinner()
    {
        var winner = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<VaultKeyRing>?>(null),
            Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(winner, ETagTwo)));
        _store.AddKeyRingAsync(Arg.Any<VaultKeyRing>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));

        using var opened = await _service.OpenOrCreateAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Equal(ETagTwo, opened.KeyRingETag);
        Assert.Equal(winner.MetadataKey.Value, opened.Session.WrappedKey.Value);
        Assert.Equal(2, _encryptor.Sessions.Count);
        var own = _encryptor.Sessions[0];
        Assert.True(own.Disposed);
        Assert.False(_encryptor.Sessions[1].Disposed);
        Assert.NotEqual(own.Inner.WrappedKey.Value, opened.Session.WrappedKey.Value);
    }

    [Fact]
    public async Task OpenOrCreateAsync_AddReturnsNullAndWinnerVanished_Throws()
    {
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));
        _store.AddKeyRingAsync(Arg.Any<VaultKeyRing>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.OpenOrCreateAsync(_owner, TestContext.Current.CancellationToken));

        Assert.True(_encryptor.Sessions.Single().Disposed);
    }

    [Fact]
    public async Task OpenOrCreateAsync_AddThrows_DisposesOwnKeyAndPropagates()
    {
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));
        _store.AddKeyRingAsync(Arg.Any<VaultKeyRing>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("storage"));

        await Assert.ThrowsAsync<TimeoutException>(() => _service.OpenOrCreateAsync(_owner, TestContext.Current.CancellationToken));

        Assert.True(_encryptor.Sessions.Single().Disposed);
    }

    [Fact]
    public async Task OpenOrCreateAsync_Existing_DoesNotCreate()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        var wrapsBefore = _harness.Provider.WrapCount;

        using var opened = await _service.OpenOrCreateAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Equal(ETagOne, opened.KeyRingETag);
        Assert.Equal(wrapsBefore, _harness.Provider.WrapCount);
        await _store.DidNotReceive().AddKeyRingAsync(Arg.Any<VaultKeyRing>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetKeyRingETagAsync_ReadsWithoutUnwrapping()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        var unwrapsBefore = _harness.Provider.UnwrapCount;

        var etag = await _service.GetKeyRingETagAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Equal(ETagOne, etag);
        Assert.Equal(unwrapsBefore, _harness.Provider.UnwrapCount);
    }

    [Fact]
    public async Task GetKeyRingETagAsync_NoRing_ReturnsNull()
    {
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));

        Assert.Null(await _service.GetKeyRingETagAsync(_owner, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task OpenedVault_Dispose_DisposesTheSession()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        var opened = await _service.OpenAsync(_owner, TestContext.Current.CancellationToken);

        opened!.Dispose();

        Assert.True(_encryptor.Sessions.Single().Disposed);
    }

    private sealed class TrackingEncryptor(IEnvelopeEncryptor inner) : IEnvelopeEncryptor
    {
        private readonly List<TrackingSession> _sessions = [];

        public List<TrackingSession> Sessions => _sessions;

        public async Task<IDataKeySession> CreateDataKeyAsync(CancellationToken cancellationToken) =>
            Track(await inner.CreateDataKeyAsync(cancellationToken));

        public async Task<IDataKeySession> OpenDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken) =>
            Track(await inner.OpenDataKeyAsync(wrappedKey, cancellationToken));

        public Task<WrappedDataKey> RewrapDataKeyAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken) =>
            inner.RewrapDataKeyAsync(wrappedKey, cancellationToken);

        private TrackingSession Track(IDataKeySession session)
        {
            var tracked = new TrackingSession(session);
            _sessions.Add(tracked);
            return tracked;
        }
    }

    private sealed class TrackingSession(IDataKeySession inner) : IDataKeySession
    {
        public IDataKeySession Inner { get; } = inner;

        public bool Disposed { get; private set; }

        public WrappedDataKey WrappedKey => Inner.WrappedKey;

        public byte[] Encrypt(EncryptionContext context, string plaintext) => Inner.Encrypt(context, plaintext);

        public string Decrypt(EncryptionContext context, byte[] envelope) => Inner.Decrypt(context, envelope);

        public byte[] ReEncrypt(EncryptionContext source, EncryptionContext target, byte[] envelope) => Inner.ReEncrypt(source, target, envelope);

        public byte[] ReEncryptTo(EncryptionContext source, IDataKeySession targetSession, EncryptionContext targetContext, byte[] envelope) =>
            Inner.ReEncryptTo(source, targetSession, targetContext, envelope);

        public void Dispose()
        {
            Disposed = true;
            Inner.Dispose();
        }
    }

    [Fact]
    public async Task OpenForReadAsync_NoRing_ReturnsNoRing()
    {
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));

        using var read = await _service.OpenForReadAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Equal(VaultReadState.NoRing, read.State);
        Assert.Null(read.Vault);
    }

    [Fact]
    public async Task OpenForReadAsync_Ring_ReturnsOpenedVault()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));

        using var read = await _service.OpenForReadAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Equal(VaultReadState.Opened, read.State);
        Assert.Equal(ETagOne, read.Vault!.KeyRingETag);
    }

    [Fact]
    public async Task OpenForReadAsync_KeyCannotUnwrapTheRing_ReturnsUnreadableAndLogsErrorWithoutKeyMaterial()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        _harness.Provider.UnwrapFailure = () => new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);
        var logger = new RecordingLogger<VaultKeyRingService>();
        var service = new VaultKeyRingService(_store, _encryptor, _time, logger);

        using var read = await service.OpenForReadAsync(_owner, TestContext.Current.CancellationToken);

        Assert.Equal(VaultReadState.Unreadable, read.State);
        Assert.Null(read.Vault);
        var record = Assert.Single(logger.Records, entry => entry.Level == LogLevel.Error);
        Assert.Contains(nameof(DecryptionFailureReason.KeyMismatch), record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(ring.MetadataKey.Value), logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenForReadAsync_KeyServiceDown_Propagates()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        _harness.Provider.UnwrapFailure = () => new TimeoutException("key vault down");

        await Assert.ThrowsAsync<TimeoutException>(() => _service.OpenForReadAsync(_owner, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenAsync_KeyCannotUnwrapTheRing_StillThrows()
    {
        var ring = await ExistingRingAsync();
        _store.GetKeyRingAsync(_owner, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(ring, ETagOne)));
        _harness.Provider.UnwrapFailure = () => new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);

        await Assert.ThrowsAsync<SecretDecryptionException>(() => _service.OpenAsync(_owner, TestContext.Current.CancellationToken));
    }
}
