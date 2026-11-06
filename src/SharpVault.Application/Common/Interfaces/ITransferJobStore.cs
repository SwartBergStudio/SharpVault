// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Common.Interfaces;

public interface ITransferJobStore
{
    Task<Versioned<TransferJob>?> GetAsync(Guid transferId, CancellationToken cancellationToken);

    Task<TransferJobPage> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<StoreWriteResult> AddWithMarkerAsync(TransferJob job, CancellationToken cancellationToken);

    Task<Versioned<TransferJob>?> ReplaceAsync(TransferJob job, string etag, CancellationToken cancellationToken);

    Task<StoreWriteResult> CloseAsync(TransferJob job, string etag, CancellationToken cancellationToken);

    Task<TransferMarker?> GetMarkerAsync(Guid sourceUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransferMarker>> ListActiveAsync(CancellationToken cancellationToken);

    Task UpsertNoticeAsync(TransferNotice notice, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransferNotice>> ListNoticesAsync(Guid userId, int limit, CancellationToken cancellationToken);

    Task<StoreWriteResult> DismissNoticeAsync(Guid userId, Guid transferId, CancellationToken cancellationToken);
}
