// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IShareGrantsApiClient
{
    Task<ApiResponse<ShareGrantListDto>> ListSecretGrantsAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);

    Task<ApiResponse<ShareGrantDto>> CreateSecretGrantAsync(Guid ownerId, Guid secretId, CreateShareGrantRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<ShareGrantDto>> ChangeSecretGrantAsync(Guid ownerId, Guid secretId, Guid recipientId, string concurrencyToken, ChangeShareGrantRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> RevokeSecretGrantAsync(Guid ownerId, Guid secretId, Guid recipientId, CancellationToken cancellationToken);

    Task<ApiResponse<ShareGrantListDto>> ListFolderGrantsAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);

    Task<ApiResponse<ShareGrantDto>> CreateFolderGrantAsync(Guid ownerId, Guid folderId, CreateShareGrantRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<ShareGrantDto>> ChangeFolderGrantAsync(Guid ownerId, Guid folderId, Guid recipientId, string concurrencyToken, ChangeShareGrantRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> RevokeFolderGrantAsync(Guid ownerId, Guid folderId, Guid recipientId, CancellationToken cancellationToken);

    Task<ApiResponse<OwnedSharePageDto>> ListOwnedSharesAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);
}
