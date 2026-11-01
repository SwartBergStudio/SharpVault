// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Users;

/// <summary>
/// TEN-5, LCK-1 and AUD-2 for the access gate: lock checks, cache, sign-in throttling and write ordering.
/// </summary>
public sealed class UserAccessGateTests : IDisposable
{
    private const string DisplayName = "Alice Example";
    private const string Email = "alice@example.test";
    private const string ETag = "etag-1";

    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserStore _store = Substitute.For<IUserStore>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly UserAccessOptions _options = new() { CacheTtl = TimeSpan.FromSeconds(5) };
    private readonly RecordingLogger<UserAccessGate> _logger = new();
    private readonly UserAccessCache _cache;
    private readonly UserAccessGate _gate;
    private readonly Guid _userId = Guid.NewGuid();

    public UserAccessGateTests()
    {
        _cache = new UserAccessCache(Options.Create(_options), _time);
        _gate = new UserAccessGate(_store, _audit, _cache, _time, Options.Create(_options), _logger);
        _store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task CheckAsync_NewUser_AddsActiveRowThenAppendsSignInAndAllows()
    {
        StoredRowIs(null);
        User? added = null;
        _store.AddAsync(Arg.Do<User>(user => added = user), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, decision);
        Assert.NotNull(added);
        Assert.Equal(_userId, added.Id);
        Assert.Equal(UserStatus.Active, added.Status);
        Assert.Equal(_time.GetUtcNow(), added.CreatedAt);
        Assert.Equal(_time.GetUtcNow(), added.LastSignInAt);
        Received.InOrder(() =>
        {
            _store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
            _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        });
        await _audit.Received(1).AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_NewUserAddRaceLost_ReloadsAndDoesNotAudit()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<User>?>(null),
            Task.FromResult<Versioned<User>?>(Stored(UserStatus.Active, Start)));
        _store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.AlreadyExists);

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, decision);
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _store.Received(2).GetAsync(_userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_NewUserAddRaceLostToLockedRow_ReturnsDenied()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<User>?>(null),
            Task.FromResult<Versioned<User>?>(Stored(UserStatus.Locked, Start)));
        _store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.AlreadyExists);

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_ActiveUserSignInNotDue_AllowsWithoutWrites()
    {
        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromMinutes(10)));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, decision);
        await AssertNoWrites();
    }

    [Fact]
    public async Task CheckAsync_ActiveUserSignInDue_ReplacesWithETagThenAppendsSignIn()
    {
        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2)));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, decision);
        Received.InOrder(() =>
        {
            _store.ReplaceAsync(Arg.Any<User>(), ETag, Arg.Any<CancellationToken>());
            _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        });
        await _store.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_SignInExactlyAtInterval_IsDue()
    {
        StoredRowIs(Stored(UserStatus.Active, Start - _options.SignInRecordInterval));

        await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        await _store.Received(1).ReplaceAsync(Arg.Any<User>(), ETag, Arg.Any<CancellationToken>());
        await _audit.Received(1).AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_SignInOneTickBeforeInterval_IsNotDue()
    {
        StoredRowIs(Stored(UserStatus.Active, Start - _options.SignInRecordInterval + TimeSpan.FromTicks(1)));

        await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        await AssertNoWrites();
    }

    [Fact]
    public async Task CheckAsync_SignInDue_RefreshesDisplayNameAndEmailButNotStatus()
    {
        var created = Start - TimeSpan.FromDays(30);
        var stored = Stored(UserStatus.Active, Start - TimeSpan.FromHours(2), "Old Name", "old@example.test", created);
        StoredRowIs(stored);
        User? replaced = null;
        _store.ReplaceAsync(Arg.Do<User>(user => replaced = user), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(StoreWriteResult.Written);

        await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.Equal(DisplayName, replaced.DisplayName);
        Assert.Equal(Email, replaced.Email);
        Assert.Equal(UserStatus.Active, replaced.Status);
        Assert.Equal(created, replaced.CreatedAt);
        Assert.Equal(_time.GetUtcNow(), replaced.LastSignInAt);
        Assert.Equal(_userId, replaced.Id);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_ReplaceConflictThenLocked_ReturnsDenied()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<User>?>(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2))),
            Task.FromResult<Versioned<User>?>(Stored(UserStatus.Locked, Start - TimeSpan.FromHours(2), etag: "etag-2")));
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Conflict);

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _store.Received(1).ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.True(_cache.TryGet(_userId, out var cached));
        Assert.Equal(UserStatus.Locked, cached.Status);
    }

    [Fact]
    public async Task CheckAsync_ReplaceConflictThenActive_AllowsWithoutAudit()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<User>?>(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2))),
            Task.FromResult<Versioned<User>?>(Stored(UserStatus.Active, Start, etag: "etag-2")));
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Conflict);

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, decision);
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_ReplaceNotFoundAndRowGone_Throws()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<User>?>(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2))),
            Task.FromResult<Versioned<User>?>(null));
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.NotFound);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        Assert.False(_cache.TryGet(_userId, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_LockedUser_DeniesWithoutAnyWrite()
    {
        StoredRowIs(Stored(UserStatus.Locked, Start - TimeSpan.FromDays(3)));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await AssertNoWrites();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_UnknownStatusValue_Denied()
    {
        StoredRowIs(Stored((UserStatus)99, Start));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await AssertNoWrites();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_LockedUser_LogsWarningWithUserIdOnly()
    {
        StoredRowIs(Stored(UserStatus.Locked, Start, "Locked Person", "locked.person@example.test"));

        await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        var warning = Assert.Single(_logger.Records, record => record.Level == LogLevel.Warning);
        Assert.Contains(_userId.ToString(), warning.Message, StringComparison.OrdinalIgnoreCase);
        var text = _logger.AllText();
        Assert.DoesNotContain(DisplayName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Locked Person", text, StringComparison.Ordinal);
        Assert.DoesNotContain("locked.person@example.test", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_AllPaths_LogsContainNoNameEmailOrIpAddress()
    {
        const string ip = "203.0.113.77";
        var user = SignedIn(ip: ip);

        StoredRowIs(null);
        await _gate.CheckAsync(user, TestContext.Current.CancellationToken);
        _cache.Invalidate(_userId);

        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2)));
        await _gate.CheckAsync(user, TestContext.Current.CancellationToken);
        _cache.Invalidate(_userId);

        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Conflict);
        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2)));
        await _gate.CheckAsync(user, TestContext.Current.CancellationToken);
        _cache.Invalidate(_userId);

        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);
        _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("audit down"));
        await Assert.ThrowsAsync<TimeoutException>(() => _gate.CheckAsync(user, TestContext.Current.CancellationToken));

        var text = _logger.AllText();
        Assert.NotEmpty(_logger.Records);
        Assert.DoesNotContain(DisplayName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ip, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_CachedActiveWithinTtl_MakesNoStoreCall()
    {
        StoredRowIs(Stored(UserStatus.Active, Start));
        await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);
        _store.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromSeconds(4));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, decision);
        Assert.Empty(_store.ReceivedCalls());
        Assert.Empty(_audit.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_CachedLockedWithinTtl_DeniesWithoutStoreCall()
    {
        StoredRowIs(Stored(UserStatus.Locked, Start));
        await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);
        _store.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromSeconds(4));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        Assert.Empty(_store.ReceivedCalls());
        Assert.Empty(_audit.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_LockedAfterCaching_DeniedOnceTtlExpires()
    {
        StoredRowIs(Stored(UserStatus.Active, Start));
        Assert.Equal(UserAccessDecision.Allowed, await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));
        StoredRowIs(Stored(UserStatus.Locked, Start));

        _time.Advance(_options.CacheTtl - TimeSpan.FromMilliseconds(1));
        Assert.Equal(UserAccessDecision.Allowed, await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        _time.Advance(TimeSpan.FromMilliseconds(2));
        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_Invalidated_ReadsFreshStatus()
    {
        StoredRowIs(Stored(UserStatus.Active, Start));
        Assert.Equal(UserAccessDecision.Allowed, await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));
        StoredRowIs(Stored(UserStatus.Locked, Start));
        _cache.Invalidate(_userId);

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
    }

    [Fact]
    public async Task CheckAsync_CachedButSignInDue_ReadsAndRecords()
    {
        _cache.Set(_userId, new CachedUserAccess(UserStatus.Active, Start - TimeSpan.FromHours(2)));
        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2)));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, decision);
        await _store.Received(1).GetAsync(_userId, Arg.Any<CancellationToken>());
        await _store.Received(1).ReplaceAsync(Arg.Any<User>(), ETag, Arg.Any<CancellationToken>());
        await _audit.Received(1).AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_CachedActiveSignInDueButNowLocked_DeniesWithoutWrites()
    {
        _cache.Set(_userId, new CachedUserAccess(UserStatus.Active, Start - TimeSpan.FromHours(2)));
        StoredRowIs(Stored(UserStatus.Locked, Start - TimeSpan.FromHours(2)));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await AssertNoWrites();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_CachedLockedUserAfterSignInInterval_DeniesWithoutStorageCalls()
    {
        StoredRowIs(Stored(UserStatus.Locked, Start - TimeSpan.FromHours(2)));
        Assert.Equal(UserAccessDecision.Denied, await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));
        _store.ClearReceivedCalls();
        _audit.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromSeconds(1));

        var decision = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Denied, decision);
        await _store.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_StoreThrows_DoesNotCacheAndPropagates()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("storage down"));

        await Assert.ThrowsAsync<TimeoutException>(() => _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        Assert.False(_cache.TryGet(_userId, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_AddThrows_DoesNotCacheAndPropagates()
    {
        StoredRowIs(null);
        _store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("storage down"));

        await Assert.ThrowsAsync<TimeoutException>(() => _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        Assert.False(_cache.TryGet(_userId, out _));
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_ReplaceThrows_DoesNotCacheAndPropagates()
    {
        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2)));
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("storage down"));

        await Assert.ThrowsAsync<TimeoutException>(() => _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        Assert.False(_cache.TryGet(_userId, out _));
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_ReloadThrows_DoesNotCacheAndPropagates()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<Versioned<User>?>(null),
            _ => throw new TimeoutException("storage down"));
        _store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.AlreadyExists);

        await Assert.ThrowsAsync<TimeoutException>(() => _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        Assert.False(_cache.TryGet(_userId, out _));
    }

    [Fact]
    public async Task CheckAsync_AuditAppendThrows_LogsErrorAndPropagates()
    {
        StoredRowIs(null);
        var failure = new TimeoutException("audit down");
        _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAsync<TimeoutException>(
            () => _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Same(failure, error.Exception);
        Assert.Contains(_userId.ToString(), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(_cache.TryGet(_userId, out _));
    }

    [Fact]
    public async Task CheckAsync_AuditAppendThrowsOnRecordedSignIn_LogsErrorAndPropagates()
    {
        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2)));
        _audit.AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("audit down"));

        await Assert.ThrowsAsync<TimeoutException>(() => _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken));

        Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.False(_cache.TryGet(_userId, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_TwoUsers_DoNotShareCacheEntries()
    {
        var otherUserId = Guid.NewGuid();
        StoredRowIs(Stored(UserStatus.Active, Start));
        _store.GetAsync(otherUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<User>?>(Stored(UserStatus.Locked, Start, id: otherUserId)));

        var first = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);
        var second = await _gate.CheckAsync(SignedIn(otherUserId), TestContext.Current.CancellationToken);
        var firstAgain = await _gate.CheckAsync(SignedIn(), TestContext.Current.CancellationToken);
        var secondAgain = await _gate.CheckAsync(SignedIn(otherUserId), TestContext.Current.CancellationToken);

        Assert.Equal(UserAccessDecision.Allowed, first);
        Assert.Equal(UserAccessDecision.Denied, second);
        Assert.Equal(UserAccessDecision.Allowed, firstAgain);
        Assert.Equal(UserAccessDecision.Denied, secondAgain);
    }

    [Theory]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData("::1", "::1")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-an-ip", null)]
    [InlineData("10.0.0.1, 192.168.0.1", null)]
    [InlineData("1.2.3.4\r\nX-Injected: 1", null)]
    public async Task CheckAsync_IpAddress_IsNormalizedIntoAuditEntry(string? supplied, string? expected)
    {
        StoredRowIs(null);
        AuditEntry? entry = null;
        _audit.AppendAsync(Arg.Do<AuditEntry>(value => entry = value), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _gate.CheckAsync(SignedIn(ip: supplied), TestContext.Current.CancellationToken);

        Assert.NotNull(entry);
        Assert.Equal(expected, entry.IpAddress);
    }

    [Fact]
    public async Task CheckAsync_HundredCharacterIpAddress_IsStoredAsNull()
    {
        StoredRowIs(null);
        AuditEntry? entry = null;
        _audit.AppendAsync(Arg.Do<AuditEntry>(value => entry = value), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _gate.CheckAsync(SignedIn(ip: new string('1', 100)), TestContext.Current.CancellationToken);

        Assert.NotNull(entry);
        Assert.Null(entry.IpAddress);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CheckAsync_AuditEntry_HasNoNameOrEmail()
    {
        StoredRowIs(null);
        AuditEntry? entry = null;
        _audit.AppendAsync(Arg.Do<AuditEntry>(value => entry = value), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _gate.CheckAsync(SignedIn(ip: "10.0.0.1"), TestContext.Current.CancellationToken);

        Assert.NotNull(entry);
        Assert.Equal(AuditEventType.SignIn, entry.EventType);
        Assert.Equal(_userId, entry.ActorId);
        Assert.Equal(_time.GetUtcNow(), entry.OccurredAt);
        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Null(entry.TargetId);
        Assert.Null(entry.Reason);
        var stringValues = typeof(AuditEntry).GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (string?)property.GetValue(entry))
            .Where(value => value is not null);
        foreach (var value in stringValues)
        {
            Assert.DoesNotContain(DisplayName, value, StringComparison.Ordinal);
            Assert.DoesNotContain(Email, value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CheckAsync_NewUserHostileClaims_StoresNormalizedValues()
    {
        StoredRowIs(null);
        User? added = null;
        _store.AddAsync(Arg.Do<User>(user => added = user), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);
        var hostileName = "Mallory\u0000\r\n" + new string('x', 10_000);

        await _gate.CheckAsync(
            new SignedInUser(_userId, hostileName, "no-at-sign", null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(added);
        Assert.Equal(UserProfileText.DisplayNameMaxLength, added.DisplayName.Length);
        Assert.DoesNotContain('\u0000', added.DisplayName);
        Assert.DoesNotContain('\n', added.DisplayName);
        Assert.Null(added.Email);
    }

    [Fact]
    public async Task CheckAsync_NullUser_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _gate.CheckAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_PassesCancellationTokenToEveryStoreCall()
    {
        using var source = new CancellationTokenSource();
        var token = source.Token;

        StoredRowIs(null);
        await _gate.CheckAsync(SignedIn(), token);
        _cache.Invalidate(_userId);
        StoredRowIs(Stored(UserStatus.Active, Start - TimeSpan.FromHours(2)));
        await _gate.CheckAsync(SignedIn(), token);
        _cache.Invalidate(_userId);
        _store.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Conflict);
        await _gate.CheckAsync(SignedIn(), token);

        var calls = _store.ReceivedCalls().Concat(_audit.ReceivedCalls()).ToList();
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Equal(token, call.GetArguments()[^1]));
    }

    [Fact]
    public void Defaults_FailClosed_StatusAndDecisionAreDenying()
    {
        Assert.Equal(UserStatus.Locked, default(UserStatus));
        Assert.Equal(UserStatus.Locked, new User().Status);
        Assert.Equal(UserAccessDecision.Denied, default(UserAccessDecision));
    }

    private SignedInUser SignedIn(Guid? id = null, string? ip = null) =>
        new(id ?? _userId, DisplayName, Email, ip);

    private void StoredRowIs(Versioned<User>? row) =>
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(row));

    private Versioned<User> Stored(
        UserStatus status,
        DateTimeOffset lastSignInAt,
        string displayName = "Stored Name",
        string? email = "stored@example.test",
        DateTimeOffset? createdAt = null,
        string etag = ETag,
        Guid? id = null) =>
        new(
            new User
            {
                Id = id ?? _userId,
                DisplayName = displayName,
                Email = email,
                Status = status,
                CreatedAt = createdAt ?? lastSignInAt,
                LastSignInAt = lastSignInAt,
            },
            etag);

    private async Task AssertNoWrites()
    {
        await _store.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().AppendAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
