// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Common.Interfaces;

public interface ITransferStagingStore
{
    Task<StageFence> TakeFenceAsync(Guid targetOwnerId, Guid transferId, CancellationToken cancellationToken);

    Task ReleaseFenceAsync(Guid targetOwnerId, Guid transferId, StageFence fence, CancellationToken cancellationToken);

    Task<IReadOnlyList<StagedItemRef>> ListIndexAsync(Guid targetOwnerId, Guid transferId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> StageSecretsAsync(Guid transferId, IReadOnlyList<StagedSecret> items, StageFence fence, CancellationToken cancellationToken);

    Task StageFoldersAsync(Guid transferId, IReadOnlyList<StagedFolder> items, StageFence fence, CancellationToken cancellationToken);

    Task StageSkippedAsync(Guid targetOwnerId, Guid transferId, Guid secretId, string sourceETag, StageFence fence, CancellationToken cancellationToken);

    Task RemoveAsync(Guid targetOwnerId, Guid transferId, IReadOnlyList<StagedItemRef> items, CancellationToken cancellationToken);

    Task<IReadOnlyList<StagedFolder>> ReadFoldersAsync(Guid targetOwnerId, Guid transferId, CancellationToken cancellationToken);

    Task<StagedSecretPage> ReadSecretsAsync(Guid targetOwnerId, Guid transferId, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublishFailure>> PublishAsync(Guid targetOwnerId, Guid transferId, IReadOnlyList<PublishGroup> groups, CancellationToken cancellationToken);
}
