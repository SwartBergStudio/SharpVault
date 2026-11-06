// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class VaultFoldersApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<VaultFoldersApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IVaultFoldersApiClient
{
    public Task<ApiResponse<FolderListDto>> ListFoldersAsync(CancellationToken cancellationToken) =>
        GetAsync<FolderListDto>(ApiRoutes.VaultFolders, cancellationToken);

    public Task<ApiResponse<FolderDto>> CreateFolderAsync(CreateFolderRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<CreateFolderRequestDto, FolderDto>(ApiRoutes.VaultFolders, request, null, cancellationToken);
    }

    public Task<ApiResponse<FolderDto>> RenameFolderAsync(Guid folderId, RenameFolderRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PutAsync<RenameFolderRequestDto, FolderDto>(ApiRoutes.VaultFolder.Bind("folderId", folderId), request, null, cancellationToken);
    }

    public Task<ApiResponse<FolderDto>> MoveFolderAsync(Guid folderId, MoveFolderRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<MoveFolderRequestDto, FolderDto>(ApiRoutes.VaultFolderMove.Bind("folderId", folderId), request, null, cancellationToken);
    }

    public Task<ApiResponse<bool>> DeleteFolderAsync(Guid folderId, FolderDeleteModeDto mode, CancellationToken cancellationToken)
    {
        var modeName = Enum.GetName(mode) ?? throw new ArgumentOutOfRangeException(nameof(mode));
        return DeleteAsync(ApiRoutes.VaultFolder.Bind("folderId", folderId) + "?" + ApiQuery.Mode + "=" + modeName, null, cancellationToken);
    }
}
