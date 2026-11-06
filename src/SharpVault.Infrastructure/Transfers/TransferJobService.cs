// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Job;

namespace SharpVault.Infrastructure.Transfers;

public sealed partial class TransferJobService(
    ITransferJobRunner runner,
    ITransferJobSignal signal,
    IOptions<TransferOptions> options,
    ILogger<TransferJobService> logger) : BackgroundService
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
            while (true)
            {
                await RunOnceAsync(stoppingToken);
                await signal.WaitAsync(settings.PollInterval, stoppingToken);
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
            await runner.RunOnceAsync(cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            LogRunFailed(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Transfer job run failed ({ExceptionType})")]
    private static partial void LogRunFailed(ILogger logger, string exceptionType);
}
