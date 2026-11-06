// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Common.Interfaces;

public interface IShareInboxStore
{
    Task AddIfAbsentAsync(ShareInboxEntry entry, CancellationToken cancellationToken);

    Task<Versioned<ShareInboxEntry>?> GetAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken);

    Task<StoreWriteResult> SetFiledFolderAsync(ShareInboxEntry entry, string etag, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken);

    Task<ShareInboxPage> ListAsync(Guid recipientId, Guid? filedFolderId, int pageSize, string? continuationToken, CancellationToken cancellationToken);
}
