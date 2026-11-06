// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.UI.Shared.Services.Clipboard;

namespace SharpVault.UI.Shared.Components.Vault;

public static class SecretStatusText
{
    public const string MovedText = "The item was moved.";

    public static string Copied(VaultUiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var seconds = ((int)settings.ClipboardClearDelay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return $"Copied. Cleared after {seconds} seconds, or when you return to this tab if you have left it.";
    }
}
