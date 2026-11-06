// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UI.Shared.Components.Vault;

public static class VaultStatusText
{
    public const string ConflictText = "This item was changed elsewhere. Reload and try again.";
    public const string InvalidFallbackText = "Check the fields and try again.";
    public const string FailedText = "That did not work. Try again.";

    public static string For<T>(ApiResponse<T> response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Status switch
        {
            ApiCallStatus.NotFound => LoadableComponentBase<T>.NotFoundText,
            ApiCallStatus.RateLimited => LoadableComponentBase<T>.RateLimitedText,
            ApiCallStatus.Conflict => ConflictText,
            ApiCallStatus.Invalid => string.IsNullOrWhiteSpace(response.Detail) ? InvalidFallbackText : response.Detail,
            ApiCallStatus.ConfigurationMismatch => LoadableComponentBase<T>.ConfigurationMismatchText,
            _ => FailedText,
        };
    }
}
