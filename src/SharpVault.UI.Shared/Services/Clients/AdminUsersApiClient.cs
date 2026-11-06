// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class AdminUsersApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<AdminUsersApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IAdminUsersApiClient
{
    public Task<ApiResponse<UserPageDto>> ListUsersAsync(int? pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        GetAsync<UserPageDto>(ApiRoutes.AdminUsers + QueryString(pageSize, continuationToken), cancellationToken);

    public Task<ApiResponse<UserSummaryDto>> LockAsync(Guid userId, LockUserRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<LockUserRequestDto, UserSummaryDto>(ApiRoutes.AdminUserLock.Bind("userId", userId), request, null, cancellationToken);
    }

    public Task<ApiResponse<UserSummaryDto>> UnlockAsync(Guid userId, LockUserRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<LockUserRequestDto, UserSummaryDto>(ApiRoutes.AdminUserUnlock.Bind("userId", userId), request, null, cancellationToken);
    }

    public Task<ApiResponse<UserCountsDto>> GetCountsAsync(Guid userId, CancellationToken cancellationToken) =>
        GetAsync<UserCountsDto>(ApiRoutes.AdminUserCounts.Bind("userId", userId), cancellationToken);

    private static string QueryString(int? pageSize, string? continuationToken)
    {
        var parts = new List<string>(2);
        if (pageSize is not null)
        {
            parts.Add($"{ApiQuery.PageSize}={pageSize.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrEmpty(continuationToken))
        {
            parts.Add($"{ApiQuery.ContinuationToken}={Uri.EscapeDataString(continuationToken)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }
}
