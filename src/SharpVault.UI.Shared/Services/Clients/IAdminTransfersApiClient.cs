// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Transfers;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IAdminTransfersApiClient
{
    Task<ApiResponse<TransferPreviewDto>> PreviewAsync(Guid sourceUserId, CancellationToken cancellationToken);

    Task<ApiResponse<TransferDto>> CreateAsync(CreateTransferRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<TransferPageDto>> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<ApiResponse<TransferDto>> GetTransferAsync(Guid transferId, CancellationToken cancellationToken);

    Task<ApiResponse<TransferDto>> ApproveAsync(Guid transferId, string concurrencyToken, CancellationToken cancellationToken);

    Task<ApiResponse<TransferDto>> RejectAsync(Guid transferId, string concurrencyToken, TransferDecisionRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<TransferDto>> AbortAsync(Guid transferId, string concurrencyToken, TransferDecisionRequestDto request, CancellationToken cancellationToken);
}
