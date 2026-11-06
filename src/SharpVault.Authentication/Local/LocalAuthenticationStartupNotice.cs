// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharpVault.Authentication.Local;

internal sealed partial class LocalAuthenticationStartupNotice(ILogger<LocalAuthenticationStartupNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogEnabled(logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = LocalAuthenticationMessages.StartupNotice)]
    private static partial void LogEnabled(ILogger logger);
}
