// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IOneTimeLinksApiClient
{
    Task<ApiResponse<OneTimeLinkSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken);

    Task<ApiResponse<OneTimeLinkSnapshotDto>> GetSnapshotAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken);

    Task<ApiResponse<OneTimeLinkDto>> CreateAsync(Guid ownerId, Guid secretId, CreateOneTimeLinkRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<OneTimeLinkPageDto>> ListAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> RevokeAsync(Guid linkId, CancellationToken cancellationToken);

    Task<ApiResponse<OneTimeLinkNoticeListDto>> ListNoticesAsync(CancellationToken cancellationToken);

    Task<ApiResponse<bool>> DismissNoticeAsync(string noticeId, CancellationToken cancellationToken);
}
