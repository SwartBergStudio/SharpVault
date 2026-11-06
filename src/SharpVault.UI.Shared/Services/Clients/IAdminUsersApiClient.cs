// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Users;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IAdminUsersApiClient
{
    Task<ApiResponse<UserPageDto>> ListUsersAsync(int? pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<ApiResponse<UserSummaryDto>> LockAsync(Guid userId, LockUserRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<UserSummaryDto>> UnlockAsync(Guid userId, LockUserRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<UserCountsDto>> GetCountsAsync(Guid userId, CancellationToken cancellationToken);
}
