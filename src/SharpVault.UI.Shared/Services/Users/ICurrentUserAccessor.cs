// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Users;

namespace SharpVault.UI.Shared.Services.Users;

public interface ICurrentUserAccessor
{
    Task<ApiResponse<CurrentUserDto>> GetAsync(CancellationToken cancellationToken);

    void Invalidate();
}
