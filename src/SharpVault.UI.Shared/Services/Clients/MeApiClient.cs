// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class MeApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<MeApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IMeApiClient
{
    public Task<ApiResponse<CurrentUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        GetAsync<CurrentUserDto>(ApiRoutes.Me, cancellationToken);

    protected override bool IsValid<T>(T value) =>
        value is not CurrentUserDto user || (user.UserId != Guid.Empty && !string.IsNullOrWhiteSpace(user.DisplayName));
}
