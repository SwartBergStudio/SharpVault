// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Directory;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class DirectoryApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<DirectoryApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IDirectoryApiClient
{
    public Task<ApiResponse<DirectoryUserListDto>> SearchUsersAsync(string search, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(search);
        return GetAsync<DirectoryUserListDto>($"{ApiRoutes.DirectoryUsers}?{ApiQuery.Search}={Uri.EscapeDataString(search)}", cancellationToken);
    }
}
