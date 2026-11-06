// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace SharpVault.IntegrationTests.Logging;

public sealed class OpenTelemetryLogRecorder : BaseProcessor<LogRecord>
{
    private readonly ConcurrentQueue<RecordedLog> _records = new();

    public IReadOnlyList<RecordedLog> Records => [.. _records];

    public void Clear() => _records.Clear();

    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var text = new StringBuilder();
        text.Append(data.FormattedMessage).Append(' ').Append(data.Body).Append(' ').Append(data.Exception);
        if (data.Attributes is not null)
        {
            foreach (var attribute in data.Attributes)
            {
                text.Append(' ').Append(attribute.Key).Append('=').Append(attribute.Value);
            }
        }

        data.ForEachScope(
            static (scope, builder) =>
            {
                foreach (var item in scope)
                {
                    builder.Append(' ').Append(item.Key).Append('=').Append(item.Value);
                }
            },
            text);
        _records.Enqueue(new RecordedLog(data.CategoryName ?? string.Empty, data.LogLevel, text.ToString()));
    }
}

public sealed record RecordedLog(string Category, LogLevel Level, string Text);
