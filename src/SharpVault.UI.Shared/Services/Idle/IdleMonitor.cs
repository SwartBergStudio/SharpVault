// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.JSInterop;

namespace SharpVault.UI.Shared.Services.Idle;

public sealed class IdleMonitor(IJSRuntime jsRuntime) : IIdleMonitor
{
    public const string ModulePath = "./_content/SharpVault.UI.Shared/js/idle-monitor.js";

    private DotNetObjectReference<IdleMonitor>? _reference;
    private IJSObjectReference? _module;
    private long _heartbeats;

    public async Task StartAsync(string signOutFormId, TimeSpan timeout, string? sessionTag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signOutFormId);

        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        _reference ??= DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("start", cancellationToken, signOutFormId, (long)timeout.TotalMilliseconds, sessionTag ?? string.Empty, _reference);
    }

    public async Task StopAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("stop");
        }
        catch (JSDisconnectedException)
        {
        }
    }

    [JSInvokable]
    public void Heartbeat() => _heartbeats++;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }

            _module = null;
        }

        _reference?.Dispose();
        _reference = null;
    }
}
