// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class RecycleBinApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<RecycleBinApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IRecycleBinApiClient
{
    public Task<ApiResponse<RecycleBinPageDto>> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        GetAsync<RecycleBinPageDto>(ApiRoutes.RecycleBin + QueryString(pageSize, continuationToken), cancellationToken);

    public Task<ApiResponse<SecretDto>> RestoreSecretAsync(Guid secretId, CancellationToken cancellationToken) =>
        PostAsync<SecretDto>(ApiRoutes.RecycleBinSecretRestore.Bind("secretId", secretId), null, cancellationToken);

    public Task<ApiResponse<FolderDto>> RestoreFolderAsync(Guid folderId, CancellationToken cancellationToken) =>
        PostAsync<FolderDto>(ApiRoutes.RecycleBinFolderRestore.Bind("folderId", folderId), null, cancellationToken);

    public Task<ApiResponse<bool>> PurgeSecretAsync(Guid secretId, CancellationToken cancellationToken) =>
        DeleteAsync(ApiRoutes.RecycleBinSecret.Bind("secretId", secretId), null, cancellationToken);

    public Task<ApiResponse<bool>> PurgeFolderAsync(Guid folderId, CancellationToken cancellationToken) =>
        DeleteAsync(ApiRoutes.RecycleBinFolder.Bind("folderId", folderId), null, cancellationToken);

    private static string QueryString(int pageSize, string? continuationToken)
    {
        var query = $"?{ApiQuery.PageSize}={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return string.IsNullOrEmpty(continuationToken) ? query : $"{query}&{ApiQuery.ContinuationToken}={Uri.EscapeDataString(continuationToken)}";
    }
}
