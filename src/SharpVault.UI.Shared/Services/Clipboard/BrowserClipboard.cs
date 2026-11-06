// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.JSInterop;

namespace SharpVault.UI.Shared.Services.Clipboard;

public sealed class BrowserClipboard(IJSRuntime jsRuntime) : IClipboard, IAsyncDisposable
{
    public const string ModulePath = "./_content/SharpVault.UI.Shared/js/clipboard.js";

    private IJSObjectReference? _module;

    public async Task WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        var module = await ModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("writeText", cancellationToken, text);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var module = await ModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("clear", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }

        _module = null;
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken) =>
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
}
