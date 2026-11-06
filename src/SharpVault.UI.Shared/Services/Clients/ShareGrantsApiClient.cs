// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class ShareGrantsApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<ShareGrantsApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IShareGrantsApiClient
{
    public Task<ApiResponse<ShareGrantListDto>> ListSecretGrantsAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        GetAsync<ShareGrantListDto>(SecretRoute(ApiRoutes.SecretGrants, ownerId, secretId), cancellationToken);

    public Task<ApiResponse<ShareGrantDto>> CreateSecretGrantAsync(Guid ownerId, Guid secretId, CreateShareGrantRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<CreateShareGrantRequestDto, ShareGrantDto>(SecretRoute(ApiRoutes.SecretGrants, ownerId, secretId), request, null, cancellationToken);
    }

    public Task<ApiResponse<ShareGrantDto>> ChangeSecretGrantAsync(Guid ownerId, Guid secretId, Guid recipientId, string concurrencyToken, ChangeShareGrantRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        ArgumentNullException.ThrowIfNull(request);
        return PutAsync<ChangeShareGrantRequestDto, ShareGrantDto>(SecretGrantRoute(ownerId, secretId, recipientId), request, concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<bool>> RevokeSecretGrantAsync(Guid ownerId, Guid secretId, Guid recipientId, CancellationToken cancellationToken) =>
        DeleteAsync(SecretGrantRoute(ownerId, secretId, recipientId), null, cancellationToken);

    public Task<ApiResponse<ShareGrantListDto>> ListFolderGrantsAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        GetAsync<ShareGrantListDto>(FolderRoute(ApiRoutes.FolderGrants, ownerId, folderId), cancellationToken);

    public Task<ApiResponse<ShareGrantDto>> CreateFolderGrantAsync(Guid ownerId, Guid folderId, CreateShareGrantRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<CreateShareGrantRequestDto, ShareGrantDto>(FolderRoute(ApiRoutes.FolderGrants, ownerId, folderId), request, null, cancellationToken);
    }

    public Task<ApiResponse<ShareGrantDto>> ChangeFolderGrantAsync(Guid ownerId, Guid folderId, Guid recipientId, string concurrencyToken, ChangeShareGrantRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        ArgumentNullException.ThrowIfNull(request);
        return PutAsync<ChangeShareGrantRequestDto, ShareGrantDto>(FolderGrantRoute(ownerId, folderId, recipientId), request, concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<bool>> RevokeFolderGrantAsync(Guid ownerId, Guid folderId, Guid recipientId, CancellationToken cancellationToken) =>
        DeleteAsync(FolderGrantRoute(ownerId, folderId, recipientId), null, cancellationToken);

    public Task<ApiResponse<OwnedSharePageDto>> ListOwnedSharesAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        GetAsync<OwnedSharePageDto>(ApiRoutes.TransferOwnedShares + OwnedSharesQueryString(pageSize, continuationToken), cancellationToken);

    private static string SecretRoute(string template, Guid ownerId, Guid secretId)
    {
        var owned = template.Bind("ownerId", ownerId);
        return owned.Bind("secretId", secretId);
    }

    private static string OwnedSharesQueryString(int pageSize, string? continuationToken)
    {
        var query = $"?{ApiQuery.PageSize}={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return string.IsNullOrEmpty(continuationToken) ? query : $"{query}&{ApiQuery.ContinuationToken}={Uri.EscapeDataString(continuationToken)}";
    }

    private static string FolderRoute(string template, Guid ownerId, Guid folderId)
    {
        var owned = template.Bind("ownerId", ownerId);
        return owned.Bind("folderId", folderId);
    }

    private static string SecretGrantRoute(Guid ownerId, Guid secretId, Guid recipientId)
    {
        var route = SecretRoute(ApiRoutes.SecretGrant, ownerId, secretId);
        return route.Bind("recipientId", recipientId);
    }

    private static string FolderGrantRoute(Guid ownerId, Guid folderId, Guid recipientId)
    {
        var route = FolderRoute(ApiRoutes.FolderGrant, ownerId, folderId);
        return route.Bind("recipientId", recipientId);
    }
}
