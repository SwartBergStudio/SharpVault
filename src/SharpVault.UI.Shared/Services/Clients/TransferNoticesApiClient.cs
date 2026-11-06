// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class TransferNoticesApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<TransferNoticesApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), ITransferNoticesApiClient
{
    public Task<ApiResponse<TransferNoticeListDto>> ListAsync(CancellationToken cancellationToken) =>
        GetAsync<TransferNoticeListDto>(ApiRoutes.TransferNotices, cancellationToken);

    public Task<ApiResponse<bool>> DismissAsync(Guid transferId, CancellationToken cancellationToken) =>
        DeleteAsync(ApiRoutes.TransferNotice.Bind("transferId", transferId), null, cancellationToken);
}
