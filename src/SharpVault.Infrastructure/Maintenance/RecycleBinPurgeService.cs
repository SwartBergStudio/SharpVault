// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Application.Features.Vault.RecycleBin;

namespace SharpVault.Infrastructure.Maintenance;

public sealed partial class RecycleBinPurgeService(
    IRecycleBinPurger purger,
    IOptions<RecycleBinPurgeOptions> options,
    TimeProvider timeProvider,
    ILogger<RecycleBinPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(settings.InitialDelay, timeProvider, stoppingToken);
            await RunOnceAsync(stoppingToken);

            using var timer = new PeriodicTimer(settings.Interval, timeProvider);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Recycle bin purge failed ({ExceptionType})")]
    private static partial void LogPurgeFailed(ILogger logger, string exceptionType);
}
