// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class OneTimeLinksApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<OneTimeLinksApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IOneTimeLinksApiClient
{
    private const string NoticeIdToken = "{noticeId}";

    public Task<ApiResponse<OneTimeLinkSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken) =>
        GetAsync<OneTimeLinkSettingsDto>(ApiRoutes.OneTimeLinkSettings, cancellationToken);

    public Task<ApiResponse<OneTimeLinkSnapshotDto>> GetSnapshotAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        PostAsync<OneTimeLinkSnapshotDto>(
            ApiRoutes.OneTimeLinkSecretSnapshot.Bind("ownerId", ownerId).Bind("secretId", secretId),
            null,
            cancellationToken);

    public Task<ApiResponse<OneTimeLinkDto>> CreateAsync(Guid ownerId, Guid secretId, CreateOneTimeLinkRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<CreateOneTimeLinkRequestDto, OneTimeLinkDto>(
            ApiRoutes.OneTimeLinkSecret.Bind("ownerId", ownerId).Bind("secretId", secretId),
            request,
            null,
            cancellationToken);
    }

    public Task<ApiResponse<OneTimeLinkPageDto>> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        GetAsync<OneTimeLinkPageDto>(ApiRoutes.OneTimeLinks + PageQuery(pageSize, continuationToken), cancellationToken);

    public Task<ApiResponse<bool>> RevokeAsync(Guid linkId, CancellationToken cancellationToken) =>
        DeleteAsync(ApiRoutes.OneTimeLink.Bind("linkId", linkId), null, cancellationToken);

    public Task<ApiResponse<OneTimeLinkNoticeListDto>> ListNoticesAsync(CancellationToken cancellationToken) =>
        GetAsync<OneTimeLinkNoticeListDto>(ApiRoutes.OneTimeLinkNotices, cancellationToken);

    public Task<ApiResponse<bool>> DismissNoticeAsync(string noticeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeId);
        return DeleteAsync(ApiRoutes.OneTimeLinkNotice.Replace(NoticeIdToken, Uri.EscapeDataString(noticeId), StringComparison.Ordinal), null, cancellationToken);
    }

    private static string PageQuery(int pageSize, string? continuationToken)
    {
        var query = $"?{ApiQuery.PageSize}={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return string.IsNullOrEmpty(continuationToken)
            ? query
            : $"{query}&{ApiQuery.ContinuationToken}={Uri.EscapeDataString(continuationToken)}";
    }
}
