// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;

namespace SharpVault.UnitTests.Support;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));
    }
}
