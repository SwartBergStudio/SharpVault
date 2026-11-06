// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IRecycleBinApiClient
{
    Task<ApiResponse<RecycleBinPageDto>> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<ApiResponse<SecretDto>> RestoreSecretAsync(Guid secretId, CancellationToken cancellationToken);

    Task<ApiResponse<FolderDto>> RestoreFolderAsync(Guid folderId, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> PurgeSecretAsync(Guid secretId, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> PurgeFolderAsync(Guid folderId, CancellationToken cancellationToken);
}
