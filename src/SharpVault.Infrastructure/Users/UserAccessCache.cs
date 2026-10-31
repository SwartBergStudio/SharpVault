// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Access;
using System.Diagnostics.CodeAnalysis;

namespace SharpVault.Infrastructure.Users;

public sealed class UserAccessCache : IUserAccessCache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    public UserAccessCache(IOptions<UserAccessOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
        _ttl = options.Value.CacheTtl;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.CacheMaxEntries });
    }

    internal int Count => _cache.Count;

    public bool TryGet(Guid userId, [NotNullWhen(true)] out CachedUserAccess? access)
    {
        if (_cache.TryGetValue(userId, out Entry? entry)
            && entry is not null
            && entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            access = entry.Access;
            return true;
        }

        access = null;
        return false;
    }

    public void Set(Guid userId, CachedUserAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (_ttl <= TimeSpan.Zero)
        {
            return;
        }

        _cache.Set(
            userId,
            new Entry(access, _timeProvider.GetUtcNow() + _ttl),
            new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = _ttl });
    }

    public void Invalidate(Guid userId) => _cache.Remove(userId);

    public void Dispose() => _cache.Dispose();

    private sealed record Entry(CachedUserAccess Access, DateTimeOffset ExpiresAt);
}
