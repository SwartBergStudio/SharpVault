// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;

namespace SharpVault.UnitTests.Support;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogRecord> _records = [];

    public IReadOnlyList<LogRecord> Records => _records;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _records.Add(new LogRecord(logLevel, formatter(state, exception), exception));

    public string AllText() => string.Join('\n', _records.Select(record => record.Message + record.Exception));

    internal sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);
}
