// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.JSInterop;
using SharpVault.UI.Shared.Services.Clipboard;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class FakeClipboard : IClipboard
{
    private readonly List<string> _writes = [];
    private int _clearCalls;

    public IReadOnlyList<string> Writes => _writes;

    public int ClearCalls => Volatile.Read(ref _clearCalls);

    public Exception? WriteFailure { get; set; }

    public Exception? ClearFailure { get; set; }

    public string Current { get; private set; } = string.Empty;

    public Task WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        _writes.Add(text);
        Current = text;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _clearCalls);
        if (ClearFailure is not null)
        {
            throw ClearFailure;
        }

        Current = string.Empty;
        return Task.CompletedTask;
    }

    public void FailWritesWithJavaScriptError(string message) => WriteFailure = new JSException(message);

    public void FailClearsWithJavaScriptError(string message) => ClearFailure = new JSException(message);
}
