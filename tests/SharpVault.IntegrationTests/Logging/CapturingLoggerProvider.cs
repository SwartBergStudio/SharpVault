// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SharpVault.IntegrationTests.Logging;

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    public void Clear() => _entries.Clear();

    public bool ContainsText(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return _entries.Any(entry =>
            entry.Message.Contains(value, StringComparison.Ordinal)
            || entry.Category.Contains(value, StringComparison.Ordinal)
            || entry.Exception?.Contains(value, StringComparison.Ordinal) == true);
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception), exception?.ToString()));
        }
    }
}
