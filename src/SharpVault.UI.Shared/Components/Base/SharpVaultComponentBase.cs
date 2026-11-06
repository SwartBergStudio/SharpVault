// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace SharpVault.UI.Shared.Components.Base;

public abstract partial class SharpVaultComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly List<IDisposable> _resources = [];
    private bool _disposed;

    protected CancellationTokenSource Cts { get; } = new();

    protected bool IsDisposed => _disposed;

    protected bool IsCommandRunning { get; private set; }

    [Inject]
    protected ILogger<SharpVaultComponentBase> Logger { get; set; } = default!;

    protected T Track<T>(T resource)
        where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    protected async Task InvokeStateChangedAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    protected async Task RunCommandAsync(Func<CancellationToken, Task> command, Action? onFailed = null)
    {
        if (IsCommandRunning)
        {
            return;
        }

        IsCommandRunning = true;
        try
        {
            await command(Cts.Token);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCommandFailed(Logger, exception.GetType().Name);
            onFailed?.Invoke();
        }
        finally
        {
            IsCommandRunning = false;
        }

        await InvokeStateChangedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Cts.CancelAsync();
        foreach (var resource in _resources)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception exception)
            {
                LogDisposeFailed(Logger, exception.GetType().Name);
            }
        }

        _resources.Clear();
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A command failed with {ExceptionType}")]
    private static partial void LogCommandFailed(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to dispose a component resource of type {ExceptionType}")]
    private static partial void LogDisposeFailed(ILogger logger, string exceptionType);
}
