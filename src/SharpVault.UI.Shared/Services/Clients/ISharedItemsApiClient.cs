// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Services.Clients;

public interface ISharedItemsApiClient
{
    Task<ApiResponse<SharedItemPageDto>> ListAsync(Guid? filedFolderId, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<ApiResponse<SharedSecretDto>> GetSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);

    Task<ApiResponse<SecretDto>> UpdateSecretAsync(Guid ownerId, Guid secretId, string concurrencyToken, UpdateSecretRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<RevealedValueDto>> RevealSecretAsync(Guid ownerId, Guid secretId, RevealSecretRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<SharedFolderContentsDto>> GetFolderAsync(Guid ownerId, Guid folderId, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> FileSecretAsync(Guid ownerId, Guid secretId, FileSharedItemRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> FileFolderAsync(Guid ownerId, Guid folderId, FileSharedItemRequestDto request, CancellationToken cancellationToken);
}
