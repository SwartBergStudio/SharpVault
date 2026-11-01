// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Users;

namespace SharpVault.UnitTests.Infrastructure.Users;

public sealed class UserAccessCacheTests : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly List<UserAccessCache> _caches = [];

    public void Dispose()
    {
        foreach (var cache in _caches)
        {
            cache.Dispose();
        }
    }

    [Fact]
    public void TryGet_Empty_ReturnsFalse()
    {
        var cache = NewCache();

        Assert.False(cache.TryGet(Guid.NewGuid(), out var access));
        Assert.Null(access);
    }

    [Fact]
    public void SetThenTryGet_WithinTtl_ReturnsEntry()
    {
        var cache = NewCache();
        var id = Guid.NewGuid();
        var stored = new CachedUserAccess(UserStatus.Active, _time.GetUtcNow());
        cache.Set(id, stored);
        _time.Advance(Ttl - TimeSpan.FromMilliseconds(1));

        Assert.True(cache.TryGet(id, out var access));
        Assert.Equal(stored, access);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void TryGet_AtTtl_ReturnsFalse()
    {
        var cache = NewCache();
        var id = Guid.NewGuid();
        cache.Set(id, new CachedUserAccess(UserStatus.Active, _time.GetUtcNow()));
        _time.Advance(Ttl);

        Assert.False(cache.TryGet(id, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void TryGet_AfterTtl_ReturnsFalse()
    {
        var cache = NewCache();
        var id = Guid.NewGuid();
        cache.Set(id, new CachedUserAccess(UserStatus.Active, _time.GetUtcNow()));
        _time.Advance(Ttl + TimeSpan.FromMilliseconds(1));

        Assert.False(cache.TryGet(id, out _));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(-5)]
    public void UserAccessCache_ZeroOrNegativeTtl_StoresNothing(int seconds)
    {
        var cache = NewCache(TimeSpan.FromSeconds(seconds));
        var id = Guid.NewGuid();

        cache.Set(id, new CachedUserAccess(UserStatus.Active, _time.GetUtcNow()));

        Assert.False(cache.TryGet(id, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Invalidate_ExistingEntry_RemovesIt()
    {
        var cache = NewCache();
        var id = Guid.NewGuid();
        cache.Set(id, new CachedUserAccess(UserStatus.Active, _time.GetUtcNow()));

        cache.Invalidate(id);

        Assert.False(cache.TryGet(id, out _));
    }

    [Fact]
    public void Invalidate_UnknownUser_DoesNotThrow()
    {
        var cache = NewCache();

        var exception = Record.Exception(() => cache.Invalidate(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public void Invalidate_OneUser_KeepsOtherUsers()
    {
        var cache = NewCache();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        cache.Set(first, new CachedUserAccess(UserStatus.Active, _time.GetUtcNow()));
        cache.Set(second, new CachedUserAccess(UserStatus.Locked, _time.GetUtcNow()));

        cache.Invalidate(first);

        Assert.False(cache.TryGet(first, out _));
        Assert.True(cache.TryGet(second, out var access));
        Assert.Equal(UserStatus.Locked, access.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Set_SameUserTwice_ReplacesEntryAndRestartsTtl()
    {
        var cache = NewCache();
        var id = Guid.NewGuid();
        cache.Set(id, new CachedUserAccess(UserStatus.Active, _time.GetUtcNow()));
        _time.Advance(TimeSpan.FromSeconds(4));

        cache.Set(id, new CachedUserAccess(UserStatus.Locked, _time.GetUtcNow()));
        _time.Advance(TimeSpan.FromSeconds(4));

        Assert.True(cache.TryGet(id, out var access));
        Assert.Equal(UserStatus.Locked, access.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UserAccessCache_MoreEntriesThanLimit_StaysBounded()
    {
        var cache = NewCache(entries: 100);

        for (var index = 0; index < 1_000; index++)
        {
            cache.Set(Guid.NewGuid(), new CachedUserAccess(UserStatus.Active, _time.GetUtcNow()));
        }

        Assert.True(cache.Count <= 100, $"Cache held {cache.Count} entries.");
    }

    [Fact]
    public void Set_NullAccess_Throws()
    {
        var cache = NewCache();

        Assert.Throws<ArgumentNullException>(() => cache.Set(Guid.NewGuid(), null!));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new UserAccessCache(null!, _time));
        Assert.Throws<ArgumentNullException>(() => new UserAccessCache(Options.Create(new UserAccessOptions()), null!));
    }

    private UserAccessCache NewCache(TimeSpan? ttl = null, int entries = 10_000)
    {
        var cache = new UserAccessCache(
            Options.Create(new UserAccessOptions { CacheTtl = ttl ?? Ttl, CacheMaxEntries = entries }),
            _time);
        _caches.Add(cache);
        return cache;
    }
}
