// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Directory;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IDirectoryApiClient
{
    Task<ApiResponse<DirectoryUserListDto>> SearchUsersAsync(string search, CancellationToken cancellationToken);
}
