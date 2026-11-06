// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UI.Shared.Components.Sharing;

public static class SharedLoadErrors
{
    public static string? ForLoad(string? errorText) =>
        errorText == LoadableComponentBase<bool>.NotFoundText ? SharedItemStatusText.AccessEnded : errorText;

    public static string ForCommand<T>(ApiResponse<T> response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Status == ApiCallStatus.NotFound ? SharedItemStatusText.AccessEnded : VaultStatusText.For(response);
    }
}
