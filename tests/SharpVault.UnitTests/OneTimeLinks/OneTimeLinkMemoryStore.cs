// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.OneTimeLinks;

namespace SharpVault.UnitTests.OneTimeLinks;

internal sealed class OneTimeLinkMemoryStore : IOneTimeLinkStore
{
    private readonly Dictionary<Guid, OneTimeLink> _links = [];
    private readonly Dictionary<Guid, OneTimeLinkIndexEntry> _index = [];
    private readonly List<OneTimeLinkNotice> _notices = [];
    private int _listIndexCalls;
    private int _consumeCalls;

    public int ActiveLinkCount => _links.Count;

    public int IndexCount => _index.Count;

    public int ListIndexCalls => _listIndexCalls;

    public int? FailListIndexOnCall { get; set; }

    public int? FailConsumeOnCall { get; set; }

    public IReadOnlyList<OneTimeLinkNotice> Notices => [.. _notices];

    public void Add(OneTimeLink link, OneTimeLinkIndexEntry index)
    {
        _links[link.Id] = link;
        _index[link.Id] = index;
    }

    public void AddIndexOnly(OneTimeLinkIndexEntry index) => _index[index.LinkId] = index;

    public Task<StoreWriteResult> CreateAsync(OneTimeLink link, OneTimeLinkIndexEntry index, CancellationToken cancellationToken)
    {
        if (_links.ContainsKey(link.Id))
        {
            return Task.FromResult(StoreWriteResult.AlreadyExists);
        }

        Add(link, index);
        return Task.FromResult(StoreWriteResult.Written);
    }

    public Task<Versioned<OneTimeLink>?> GetAsync(Guid linkId, CancellationToken cancellationToken)
    {
        Versioned<OneTimeLink>? found = null;
        if (_links.TryGetValue(linkId, out var link))
        {
            found = new Versioned<OneTimeLink>(link, ETagOf(linkId));
        }

        return Task.FromResult(found);
    }

    public Task<bool> TryConsumeAsync(Guid linkId, string etag, CancellationToken cancellationToken)
    {
        _consumeCalls++;
        if (_consumeCalls == FailConsumeOnCall)
        {
            throw new InvalidOperationException("store unavailable during consume");
        }

        if (!_links.ContainsKey(linkId) || etag != ETagOf(linkId))
        {
            return Task.FromResult(false);
        }

        _links.Remove(linkId);
        return Task.FromResult(true);
    }

    public Task<OneTimeLinkIndexEntry?> GetIndexEntryAsync(Guid creatorId, Guid linkId, CancellationToken cancellationToken)
    {
        OneTimeLinkIndexEntry? found = null;
        if (_index.TryGetValue(linkId, out var entry) && entry.CreatorId == creatorId)
        {
            found = entry;
        }

        return Task.FromResult(found);
    }

    public Task RemoveBookkeepingAsync(Guid creatorId, Guid linkId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (_index.TryGetValue(linkId, out var entry) && entry.CreatorId == creatorId)
        {
            _index.Remove(linkId);
        }

        return Task.CompletedTask;
    }

    public Task<OneTimeLinkIndexPage> ListIndexAsync(Guid creatorId, DateTimeOffset? activeAt, int pageSize, Guid? afterLinkId, CancellationToken cancellationToken)
    {
        _listIndexCalls++;
        if (_listIndexCalls == FailListIndexOnCall)
        {
            throw new InvalidOperationException("store unavailable during list");
        }

        var rows = _index.Values
            .Where(entry => entry.CreatorId == creatorId)
            .Where(entry => activeAt is null || entry.ExpiresAt > activeAt)
            .Where(entry => afterLinkId is null || entry.LinkId.CompareTo(afterLinkId.Value) > 0)
            .OrderBy(entry => entry.LinkId)
            .ToList();
        var items = rows.Take(pageSize).ToList();
        var next = rows.Count > pageSize ? (Guid?)items[^1].LinkId : null;
        return Task.FromResult(new OneTimeLinkIndexPage(items, next));
    }

    public Task AddNoticeAsync(OneTimeLinkNotice notice, CancellationToken cancellationToken)
    {
        _notices.Add(notice);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OneTimeLinkNotice>> ListNoticesAsync(Guid creatorId, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OneTimeLinkNotice>>(_notices.Where(notice => notice.CreatorId == creatorId).Take(limit).ToList());

    public Task<StoreWriteResult> DismissNoticeAsync(Guid creatorId, string noticeId, CancellationToken cancellationToken)
    {
        var removed = _notices.RemoveAll(notice => notice.CreatorId == creatorId && notice.NoticeId == noticeId);
        return Task.FromResult(removed > 0 ? StoreWriteResult.Written : StoreWriteResult.NotFound);
    }

    public Task<OneTimeLinkExpiryPage> ListExpiryBucketAsync(DateTimeOffset hour, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The expiry buckets are exercised by the purger tests, not by this store.");

    public Task DeleteExpiredAsync(OneTimeLinkExpiryEntry entry, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The expiry buckets are exercised by the purger tests, not by this store.");

    private static string ETagOf(Guid linkId) => $"W/\"{linkId:N}\"";
}
