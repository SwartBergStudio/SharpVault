// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Clipboard;

public sealed record VaultUiSettings(TimeSpan ClipboardClearDelay, TimeSpan RevealAutoHideDelay)
{
    public static VaultUiSettings Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
}
