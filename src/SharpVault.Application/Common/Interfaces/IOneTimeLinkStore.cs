// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.OneTimeLinks;

namespace SharpVault.Application.Common.Interfaces;

public interface IOneTimeLinkStore
{
    Task<StoreWriteResult> CreateAsync(OneTimeLink link, OneTimeLinkIndexEntry index, CancellationToken cancellationToken);

    Task<Versioned<OneTimeLink>?> GetAsync(Guid linkId, CancellationToken cancellationToken);

    Task<bool> TryConsumeAsync(Guid linkId, string etag, CancellationToken cancellationToken);

    Task<OneTimeLinkIndexEntry?> GetIndexEntryAsync(Guid creatorId, Guid linkId, CancellationToken cancellationToken);

    Task RemoveBookkeepingAsync(Guid creatorId, Guid linkId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<OneTimeLinkIndexPage> ListIndexAsync(Guid creatorId, DateTimeOffset? activeAt, int pageSize, Guid? afterLinkId, CancellationToken cancellationToken);

    Task AddNoticeAsync(OneTimeLinkNotice notice, CancellationToken cancellationToken);

    Task<IReadOnlyList<OneTimeLinkNotice>> ListNoticesAsync(Guid creatorId, int limit, CancellationToken cancellationToken);

    Task<StoreWriteResult> DismissNoticeAsync(Guid creatorId, string noticeId, CancellationToken cancellationToken);

    Task<OneTimeLinkExpiryPage> ListExpiryBucketAsync(DateTimeOffset hour, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task DeleteExpiredAsync(OneTimeLinkExpiryEntry entry, CancellationToken cancellationToken);
}
