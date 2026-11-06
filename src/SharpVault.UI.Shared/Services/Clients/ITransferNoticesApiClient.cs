// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Transfers;

namespace SharpVault.UI.Shared.Services.Clients;

public interface ITransferNoticesApiClient
{
    Task<ApiResponse<TransferNoticeListDto>> ListAsync(CancellationToken cancellationToken);

    Task<ApiResponse<bool>> DismissAsync(Guid transferId, CancellationToken cancellationToken);
}
