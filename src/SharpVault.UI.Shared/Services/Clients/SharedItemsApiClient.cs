// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class SharedItemsApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<SharedItemsApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), ISharedItemsApiClient
{
    public Task<ApiResponse<SharedItemPageDto>> ListAsync(Guid? filedFolderId, int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        var filter = filedFolderId is null ? string.Empty : $"{ApiQuery.FiledFolderId}={filedFolderId.Value:D}&";
        return GetAsync<SharedItemPageDto>(ApiRoutes.SharedItems + "?" + filter + PagingQuery(pageSize, continuationToken), cancellationToken);
    }

    public Task<ApiResponse<SharedSecretDto>> GetSecretAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        GetAsync<SharedSecretDto>(SecretRoute(ApiRoutes.SharedSecret, ownerId, secretId), cancellationToken);

    public Task<ApiResponse<SecretDto>> UpdateSecretAsync(Guid ownerId, Guid secretId, string concurrencyToken, UpdateSecretRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        ArgumentNullException.ThrowIfNull(request);
        return PutAsync<UpdateSecretRequestDto, SecretDto>(SecretRoute(ApiRoutes.SharedSecret, ownerId, secretId), request, concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<RevealedValueDto>> RevealSecretAsync(Guid ownerId, Guid secretId, RevealSecretRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<RevealSecretRequestDto, RevealedValueDto>(SecretRoute(ApiRoutes.SharedSecretReveal, ownerId, secretId), request, null, cancellationToken);
    }

    public Task<ApiResponse<SharedFolderContentsDto>> GetFolderAsync(Guid ownerId, Guid folderId, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        GetAsync<SharedFolderContentsDto>(FolderRoute(ApiRoutes.SharedFolder, ownerId, folderId) + "?" + PagingQuery(pageSize, continuationToken), cancellationToken);

    public Task<ApiResponse<bool>> FileSecretAsync(Guid ownerId, Guid secretId, FileSharedItemRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PutAsync<FileSharedItemRequestDto, bool>(SecretRoute(ApiRoutes.SharedSecretFiling, ownerId, secretId), request, null, cancellationToken);
    }

    public Task<ApiResponse<bool>> FileFolderAsync(Guid ownerId, Guid folderId, FileSharedItemRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PutAsync<FileSharedItemRequestDto, bool>(FolderRoute(ApiRoutes.SharedFolderFiling, ownerId, folderId), request, null, cancellationToken);
    }

    private static string SecretRoute(string template, Guid ownerId, Guid secretId)
    {
        var owned = template.Bind("ownerId", ownerId);
        return owned.Bind("secretId", secretId);
    }

    private static string FolderRoute(string template, Guid ownerId, Guid folderId)
    {
        var owned = template.Bind("ownerId", ownerId);
        return owned.Bind("folderId", folderId);
    }

    private static string PagingQuery(int pageSize, string? continuationToken)
    {
        var query = $"{ApiQuery.PageSize}={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return string.IsNullOrEmpty(continuationToken) ? query : $"{query}&{ApiQuery.ContinuationToken}={Uri.EscapeDataString(continuationToken)}";
    }
}
