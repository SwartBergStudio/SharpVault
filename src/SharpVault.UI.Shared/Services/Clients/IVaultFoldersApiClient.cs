// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IVaultFoldersApiClient
{
    Task<ApiResponse<FolderListDto>> ListFoldersAsync(CancellationToken cancellationToken);

    Task<ApiResponse<FolderDto>> CreateFolderAsync(CreateFolderRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<FolderDto>> RenameFolderAsync(Guid folderId, RenameFolderRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<FolderDto>> MoveFolderAsync(Guid folderId, MoveFolderRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> DeleteFolderAsync(Guid folderId, FolderDeleteModeDto mode, CancellationToken cancellationToken);
}
