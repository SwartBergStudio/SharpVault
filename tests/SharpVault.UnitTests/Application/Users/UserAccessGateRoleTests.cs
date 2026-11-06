// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Users;

[Trait("Category", "Security")]
public sealed class UserAccessGateRoleTests : IDisposable
{
    private const string ETag = "etag-1";

    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserStore _store = Substitute.For<IUserStore>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly UserAccessOptions _options = new() { CacheTtl = TimeSpan.FromSeconds(5) };
    private readonly UserAccessCache _cache;
    private readonly UserAccessGate _gate;
    private readonly Guid _userId = Guid.NewGuid();

    public UserAccessGateRoleTests()
    {
        _cache = new UserAccessCache(Options.Create(_options), _time);
        _gate = new UserAccessGate(_store, _audit, _cache, _time, Options.Create(_options), new RecordingLogger<UserAccessGate>());
        _store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task Gate_FirstAdminToken_WritesTheFlagOnce()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: false, observedAt: null));
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), ETag, Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        var first = await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);
        var second = await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, first);
        Assert.Equal(UserAccessDecision.Allowed, second);
        Assert.NotNull(replaced);
        Assert.True(replaced.IsAdmin);
        Assert.Equal(Start, replaced.RoleObservedAt);
        await _store.Received(1).ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _store.Received(1).GetAsync(_userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gate_RoleOnlyWrite_ChangesNothingButTheFlagAndItsTime()
    {
        var stored = Stored(UserStatus.Active, isAdmin: false, observedAt: null);
        StoredRowIs(stored);
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), ETag, Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start, displayName: "Token Name"), TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.Equal(stored.Value.DisplayName, replaced.DisplayName);
        Assert.Equal(stored.Value.Email, replaced.Email);
        Assert.Equal(stored.Value.Status, replaced.Status);
        Assert.Equal(stored.Value.CreatedAt, replaced.CreatedAt);
        Assert.Equal(stored.Value.LastSignInAt, replaced.LastSignInAt);
    }

    [Fact]
    public async Task Gate_UnchangedRole_MakesNoExtraStorageCall()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: true, observedAt: Start.AddMinutes(-5)));

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);
        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);

        await _store.DidNotReceive().ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.Received(1).GetAsync(_userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gate_NonAdminTokenOnNonAdminRow_NeverWrites()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: false, observedAt: null));

        await _gate.CheckAsync(SignedIn(isAdmin: false, issuedAt: Start), TestContext.Current.CancellationToken);

        await _store.DidNotReceive().ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gate_NewerTokenWithoutRole_ClearsTheFlag()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: true, observedAt: Start.AddMinutes(-10)));
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), ETag, Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: false, issuedAt: Start), TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.False(replaced.IsAdmin);
        Assert.Equal(Start, replaced.RoleObservedAt);
    }

    [Fact]
    public async Task Gate_OldAdminToken_DoesNotRestoreTheFlag()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: false, observedAt: Start));

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start.AddMinutes(-10)), TestContext.Current.CancellationToken);

        await _store.DidNotReceive().ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gate_OldAdminTokenWhileSignInIsDue_KeepsTheNewerObservation()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: false, observedAt: Start, lastSignInAt: Start - TimeSpan.FromHours(3)));
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), ETag, Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start.AddMinutes(-10)), TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.False(replaced.IsAdmin);
        Assert.Equal(Start, replaced.RoleObservedAt);
    }

    [Fact]
    public async Task Gate_NewerAdminToken_SetsTheFlagAgain()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: false, observedAt: Start.AddMinutes(-10)));
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), ETag, Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.True(replaced.IsAdmin);
        Assert.Equal(Start, replaced.RoleObservedAt);
    }

    [Fact]
    public async Task Gate_LockedUser_NeverWritesARole()
    {
        StoredRowIs(Stored(UserStatus.Locked, isAdmin: false, observedAt: null));

        var decision = await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await _store.DidNotReceive().ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gate_MissingIat_NeverWritesARole()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: false, observedAt: null));

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: null), TestContext.Current.CancellationToken);

        await _store.DidNotReceive().ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gate_CachedEntryWithNewerObservation_ReadsTheRow()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: true, observedAt: Start.AddMinutes(-10)));
        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start.AddMinutes(-5)), TestContext.Current.CancellationToken);
        await _store.Received(1).GetAsync(_userId, Arg.Any<CancellationToken>());

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start.AddMinutes(-5)), TestContext.Current.CancellationToken);
        await _store.Received(1).GetAsync(_userId, Arg.Any<CancellationToken>());

        await _gate.CheckAsync(SignedIn(isAdmin: false, issuedAt: Start), TestContext.Current.CancellationToken);

        await _store.Received(2).GetAsync(_userId, Arg.Any<CancellationToken>());
        await _store.Received(1).ReplaceAsync(Arg.Is<User>(user => !user.IsAdmin), ETag, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignIn_KeepsTheAdminFlag()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: true, observedAt: Start.AddMinutes(-10), lastSignInAt: Start - TimeSpan.FromHours(3)));
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), ETag, Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.True(replaced.IsAdmin);
        Assert.Equal(Start.AddMinutes(-10), replaced.RoleObservedAt);
        Assert.Equal(Start, replaced.LastSignInAt);
        await _audit.Received(1).AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignIn_NonAdminTokenWithoutIat_KeepsTheStoredFlag()
    {
        StoredRowIs(Stored(UserStatus.Active, isAdmin: true, observedAt: Start.AddMinutes(-10), lastSignInAt: Start - TimeSpan.FromHours(3)));
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), ETag, Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: false, issuedAt: null), TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.True(replaced.IsAdmin);
        Assert.Equal(Start.AddMinutes(-10), replaced.RoleObservedAt);
    }

    [Fact]
    public async Task Provision_RecordsTheAdminFlag()
    {
        StoredRowIs(null);
        User? added = null;
        _store.AddAsync(Arg.Do<User>(user => added = user), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);

        Assert.NotNull(added);
        Assert.True(added.IsAdmin);
        Assert.Equal(Start, added.RoleObservedAt);
    }

    [Fact]
    public async Task Provision_TokenWithoutIat_RecordsNoAdminFlag()
    {
        StoredRowIs(null);
        User? added = null;
        _store.AddAsync(Arg.Do<User>(user => added = user), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: null), TestContext.Current.CancellationToken);

        Assert.NotNull(added);
        Assert.False(added.IsAdmin);
        Assert.Null(added.RoleObservedAt);
    }

    [Fact]
    public async Task Gate_RoleWriteConflict_Reloads()
    {
        var original = Stored(UserStatus.Active, isAdmin: false, observedAt: null);
        var reloaded = Stored(UserStatus.Locked, isAdmin: false, observedAt: null, etag: "etag-2");
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<User>?>(original),
            Task.FromResult<Versioned<User>?>(reloaded));
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Conflict);

        var decision = await _gate.CheckAsync(SignedIn(isAdmin: true, issuedAt: Start), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await _store.Received(2).GetAsync(_userId, Arg.Any<CancellationToken>());
        await _store.Received(1).ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private SignedInUser SignedIn(bool isAdmin, DateTimeOffset? issuedAt, string displayName = "Alice Example") =>
        new(_userId, displayName, "alice@example.test", null, isAdmin, issuedAt);

    private void StoredRowIs(Versioned<User>? row) =>
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(row));

    private Versioned<User> Stored(
        UserStatus status,
        bool isAdmin,
        DateTimeOffset? observedAt,
        DateTimeOffset? lastSignInAt = null,
        string etag = ETag) =>
        new(
            new User
            {
                Id = _userId,
                DisplayName = "Stored Name",
                Email = "stored@example.test",
                Status = status,
                CreatedAt = Start - TimeSpan.FromDays(30),
                LastSignInAt = lastSignInAt ?? Start - TimeSpan.FromMinutes(10),
                IsAdmin = isAdmin,
                RoleObservedAt = observedAt,
            },
            etag);
}
