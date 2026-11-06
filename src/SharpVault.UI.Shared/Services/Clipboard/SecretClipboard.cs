// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace SharpVault.UI.Shared.Services.Clipboard;

public sealed partial class SecretClipboard(
    IClipboard clipboard,
    VaultUiSettings settings,
    TimeProvider timeProvider,
    ILogger<SecretClipboard> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _pendingClear;

    public async Task<bool> CopyAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            await clipboard.WriteTextAsync(text, cancellationToken);
        }
        catch (JSException exception)
        {
            LogFailure(exception, LogWriteFailed);
            return false;
        }

        await CancelPendingAsync();
        var source = new CancellationTokenSource();
        _pendingClear = source;
        _ = ClearWhenDueAsync(source);
        return true;
    }

    public async Task ClearNowAsync(CancellationToken cancellationToken)
    {
        var pending = Interlocked.Exchange(ref _pendingClear, null);
        if (pending is null)
        {
            return;
        }

        await pending.CancelAsync();
        pending.Dispose();
        await TryClearAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await ClearNowAsync(CancellationToken.None);
    }

    private async Task CancelPendingAsync()
    {
        var pending = Interlocked.Exchange(ref _pendingClear, null);
        if (pending is null)
        {
            return;
        }

        await pending.CancelAsync();
        pending.Dispose();
    }

    private async Task ClearWhenDueAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(settings.ClipboardClearDelay, timeProvider, source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _pendingClear, null, source) != source)
        {
            return;
        }

        source.Dispose();
        await TryClearAsync(CancellationToken.None);
    }

    private async Task TryClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            await clipboard.ClearAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or OperationCanceledException)
        {
            LogFailure(exception, LogClearFailed);
        }
    }

    private void LogFailure(Exception exception, Action<ILogger, string> log)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            log(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Writing to the clipboard failed with {ExceptionType}")]
    private static partial void LogWriteFailed(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clearing the clipboard failed with {ExceptionType}")]
    private static partial void LogClearFailed(ILogger logger, string exceptionType);
}
