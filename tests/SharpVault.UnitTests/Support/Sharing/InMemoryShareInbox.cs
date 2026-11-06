// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;

namespace SharpVault.UnitTests.Support.Sharing;

internal sealed class InMemoryShareInbox(List<string>? callLog = null) : IShareInboxStore
{
    private readonly Dictionary<ShareInboxEntry, int> _sequence = [];

    public List<ShareInboxEntry> Rows { get; } = [];

    public int PageSizeCap { get; set; } = int.MaxValue;

    public Exception? RemoveFailure { get; set; }

    public List<int> RequestedPageSizes { get; } = [];

    public Task AddIfAbsentAsync(ShareInboxEntry entry, CancellationToken cancellationToken)
    {
        callLog?.Add("inbox.add");
        if (!Rows.Any(row => Matches(row, entry.RecipientId, entry.OwnerId, entry.ItemKind, entry.ItemId)))
        {
            Rows.Add(entry);
            _sequence[entry] = _sequence.Count + 1;
        }

        return Task.CompletedTask;
    }

    public Task<Versioned<ShareInboxEntry>?> GetAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken)
    {
        var row = Rows.FirstOrDefault(candidate => Matches(candidate, recipientId, ownerId, kind, itemId));
        return Task.FromResult(row is null ? null : new Versioned<ShareInboxEntry>(row, "etag"));
    }

    public Task<StoreWriteResult> SetFiledFolderAsync(ShareInboxEntry entry, string etag, CancellationToken cancellationToken) =>
        Task.FromResult(StoreWriteResult.Written);

    public Task<bool> RemoveAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken)
    {
        callLog?.Add("inbox.remove");
        if (RemoveFailure is not null)
        {
            throw RemoveFailure;
        }

        return Task.FromResult(Rows.RemoveAll(row => Matches(row, recipientId, ownerId, kind, itemId)) > 0);
    }

    public Task<ShareInboxPage> ListAsync(Guid recipientId, Guid? filedFolderId, int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        RequestedPageSizes.Add(pageSize);
        var after = continuationToken is null ? 0 : int.Parse(continuationToken, System.Globalization.CultureInfo.InvariantCulture);
        var remaining = Rows
            .Where(row => row.RecipientId == recipientId && _sequence[row] > after)
            .OrderBy(row => _sequence[row])
            .ToList();
        var take = Math.Min(pageSize, PageSizeCap);
        var page = remaining.Take(take).ToList();
        var items = page.Select(row => new Versioned<ShareInboxEntry>(row, "etag")).ToList();
        var next = remaining.Count > take ? _sequence[page[^1]].ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new ShareInboxPage(items, next));
    }

    private static bool Matches(ShareInboxEntry row, Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId) =>
        row.RecipientId == recipientId && row.OwnerId == ownerId && row.ItemKind == kind && row.ItemId == itemId;
}
