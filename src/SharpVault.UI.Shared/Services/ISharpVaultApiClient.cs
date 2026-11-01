// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Models;

namespace SharpVault.UI.Shared.Services;

public interface ISharpVaultApiClient
{
    Task<ApiResponse<CurrentUserProfile>> GetCurrentUserAsync(CancellationToken cancellationToken);
}
