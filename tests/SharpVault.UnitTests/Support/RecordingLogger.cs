// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;

namespace SharpVault.UnitTests.Support;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogRecord> _records = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<LogRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var record = new LogRecord(logLevel, formatter(state, exception), exception);
        lock (_gate)
        {
            _records.Add(record);
        }
    }

    public string AllText() => string.Join('\n', Records.Select(record => record.Message + record.Exception));

    internal sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);
}
