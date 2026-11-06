// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class AdminTransfersApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<AdminTransfersApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IAdminTransfersApiClient
{
    public Task<ApiResponse<TransferPreviewDto>> PreviewAsync(Guid sourceUserId, CancellationToken cancellationToken) =>
        GetAsync<TransferPreviewDto>(ApiRoutes.AdminTransferPreview + $"?{ApiQuery.SourceUserId}={sourceUserId:D}", cancellationToken);

    public Task<ApiResponse<TransferDto>> CreateAsync(CreateTransferRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<CreateTransferRequestDto, TransferDto>(ApiRoutes.AdminTransfers, request, null, cancellationToken);
    }

    public Task<ApiResponse<TransferPageDto>> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        GetAsync<TransferPageDto>(ApiRoutes.AdminTransfers + QueryString(pageSize, continuationToken), cancellationToken);

    public Task<ApiResponse<TransferDto>> GetTransferAsync(Guid transferId, CancellationToken cancellationToken) =>
        GetAsync<TransferDto>(ApiRoutes.AdminTransfer.Bind("transferId", transferId), cancellationToken);

    public Task<ApiResponse<TransferDto>> ApproveAsync(Guid transferId, string concurrencyToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        return PostAsync<TransferDto>(ApiRoutes.AdminTransferApprove.Bind("transferId", transferId), concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<TransferDto>> RejectAsync(Guid transferId, string concurrencyToken, TransferDecisionRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<TransferDecisionRequestDto, TransferDto>(ApiRoutes.AdminTransferReject.Bind("transferId", transferId), request, concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<TransferDto>> AbortAsync(Guid transferId, string concurrencyToken, TransferDecisionRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<TransferDecisionRequestDto, TransferDto>(ApiRoutes.AdminTransferAbort.Bind("transferId", transferId), request, concurrencyToken, cancellationToken);
    }

    private static string QueryString(int pageSize, string? continuationToken)
    {
        var query = $"?{ApiQuery.PageSize}={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return string.IsNullOrEmpty(continuationToken) ? query : $"{query}&{ApiQuery.ContinuationToken}={Uri.EscapeDataString(continuationToken)}";
    }
}
