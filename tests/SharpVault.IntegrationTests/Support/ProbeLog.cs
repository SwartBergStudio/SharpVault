// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;

namespace SharpVault.IntegrationTests.Support;

public static partial class ProbeLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Probe message {Marker}")]
    public static partial void Information(ILogger logger, string marker);

    [LoggerMessage(Level = LogLevel.Error, Message = "Probe failure")]
    public static partial void Failure(ILogger logger, Exception exception);
}
