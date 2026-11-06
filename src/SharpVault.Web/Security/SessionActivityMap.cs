// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;

namespace SharpVault.Web.Security;

public sealed class SessionActivityMap(TimeProvider timeProvider)
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, SessionActivity> _entries = new(StringComparer.Ordinal);
    private DateTimeOffset _lastPrune;

    public int Count => _entries.Count;

    public void Start(string sessionId, DateTimeOffset expiresAt)
    {
        var now = timeProvider.GetUtcNow();
        _entries.TryAdd(sessionId, new SessionActivity(now, expiresAt));
        PruneIfDue(now);
    }

    public void Touch(string sessionId, DateTimeOffset expiresAt)
    {
        var now = timeProvider.GetUtcNow();
        _entries.AddOrUpdate(
            sessionId,
            _ => new SessionActivity(now, expiresAt),
            (_, existing) => existing with { LastActivity = now });
    }

    public TimeSpan IdleFor(string sessionId) =>
        _entries.TryGetValue(sessionId, out var entry)
            ? timeProvider.GetUtcNow() - entry.LastActivity
            : TimeSpan.MaxValue;

    public int RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();
        var removed = 0;
        foreach (var (sessionId, entry) in _entries)
        {
            if (entry.ExpiresAt <= now && _entries.TryRemove(sessionId, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private void PruneIfDue(DateTimeOffset now)
    {
        if (now - _lastPrune < PruneInterval)
        {
            return;
        }

        _lastPrune = now;
        RemoveExpired();
    }

    private sealed record SessionActivity(DateTimeOffset LastActivity, DateTimeOffset ExpiresAt);
}
