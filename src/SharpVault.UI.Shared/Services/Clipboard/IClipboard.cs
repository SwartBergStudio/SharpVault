// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Clipboard;

public interface IClipboard
{
    Task WriteTextAsync(string text, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
