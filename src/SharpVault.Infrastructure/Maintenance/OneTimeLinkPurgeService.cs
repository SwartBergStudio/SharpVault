// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Application.Features.OneTimeLinks.Common;

namespace SharpVault.Infrastructure.Maintenance;

public sealed partial class OneTimeLinkPurgeService(
    IOneTimeLinkPurger purger,
    IOptions<OneTimeLinkOptions> options,
    TimeProvider timeProvider,
    ILogger<OneTimeLinkPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.PurgeEnabled)
        {
            return;
        }

        try
        {
            await Task.Delay(settings.PurgeInitialDelay, timeProvider, stoppingToken);
            await RunOnceAsync(stoppingToken);

            using var timer = new PeriodicTimer(settings.PurgeInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await purger.PurgeExpiredAsync(cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            LogPurgeFailed(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "One-time link purge failed ({ExceptionType})")]
    private static partial void LogPurgeFailed(ILogger logger, string exceptionType);
}
