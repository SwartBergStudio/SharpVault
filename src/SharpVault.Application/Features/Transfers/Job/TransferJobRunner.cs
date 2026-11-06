// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed partial class TransferJobRunner(
    ITransferJobStore jobs,
    IMaintenanceLeaseStore leases,
    TransferPreflightPhase preflight,
    TransferStagePhase stage,
    TransferReleasePhase release,
    TransferPublishPhase publish,
    TransferCompletionPhase completion,
    TransferLifecyclePhase lifecycle,
    TimeProvider timeProvider,
    IOptions<TransferOptions> options,
    ILogger<TransferJobRunner> logger) : ITransferJobRunner
{
    public const string LeaseName = "transfer-jobs";

    private const int MaxStepsPerJobRun = 12;
    private const int BackoffBaseSeconds = 30;
    private const int MaxBackoffExponent = 10;
    private const int ErrorLogAttempts = 5;

    private readonly string _holderId = Guid.NewGuid().ToString("D");

    public async Task<TransferRunResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var due = await DueJobsAsync(cancellationToken);
        if (due.Count == 0)
        {
            return TransferRunResult.Idle;
        }

        var settings = options.Value;
        var lease = await leases.TryAcquireAsync(LeaseName, _holderId, settings.LeaseDuration, cancellationToken);
        if (lease is null)
        {
            return TransferRunResult.Skipped;
        }

        var keeper = new TransferLeaseKeeper(leases, timeProvider, settings.LeaseDuration, lease);
        try
        {
            var reloaded = await LoadDueAsync(due.Select(job => job.Value.Id), cancellationToken);
            foreach (var job in reloaded)
            {
                await RunJobAsync(job, keeper, cancellationToken);
            }
        }
        catch (TransferLeaseLostException)
        {
            LogLeaseLost(logger);
        }
        finally
        {
            await ReleaseLeaseAsync(keeper, cancellationToken);
        }

        return TransferRunResult.Ran;
    }

    private async Task<List<Versioned<TransferJob>>> DueJobsAsync(CancellationToken cancellationToken)
    {
        var markers = await jobs.ListActiveAsync(cancellationToken);
        return await LoadDueAsync(markers.Select(marker => marker.TransferId), cancellationToken);
    }

    private async Task<List<Versioned<TransferJob>>> LoadDueAsync(IEnumerable<Guid> transferIds, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = new List<Versioned<TransferJob>>();
        foreach (var transferId in transferIds)
        {
            var stored = await jobs.GetAsync(transferId, cancellationToken);
            if (stored is not null && IsDue(stored.Value, now))
            {
                due.Add(stored);
            }
        }

        return [.. due.OrderBy(job => job.Value.RequestedAt)];
    }

    private static bool IsDue(TransferJob job, DateTimeOffset now)
    {
        switch (job.State)
        {
            case TransferState.PendingApproval:
                return now >= job.ExpiresAt;
            case TransferState.Aborted:
                return job.CleanupPending && IsRetryDue(job, now);
            case TransferState.Rejected:
            case TransferState.Expired:
            case TransferState.Completed:
                return false;
            default:
                return IsRetryDue(job, now);
        }
    }

    private static bool IsRetryDue(TransferJob job, DateTimeOffset now) => job.NextAttemptAt is null || job.NextAttemptAt <= now;

    private async Task RunJobAsync(Versioned<TransferJob> job, TransferLeaseKeeper keeper, CancellationToken cancellationToken)
    {
        var cursor = new TransferJobCursor(jobs, job);
        try
        {
            for (var step = 0; step < MaxStepsPerJobRun && IsDue(cursor.Job, timeProvider.GetUtcNow()); step++)
            {
                var before = cursor.Job.State;
                await DispatchAsync(cursor, keeper, cancellationToken);
                if (cursor.Job.State == before || TransferRules.IsFinal(cursor.Job.State))
                {
                    break;
                }
            }

            await ClearFailureAsync(cursor, cancellationToken);
        }
        catch (TransferJobChangedException)
        {
            LogJobChanged(logger, job.Value.Id);
        }
        catch (Exception exception) when (exception is not TransferLeaseLostException && !cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(job.Value.Id, exception, cancellationToken);
        }
    }

    private Task DispatchAsync(TransferJobCursor cursor, TransferLeaseKeeper keeper, CancellationToken cancellationToken) => cursor.Job.State switch
    {
        TransferState.PendingApproval => lifecycle.ExpireAsync(cursor, cancellationToken),
        TransferState.Approved => preflight.RunAsync(cursor, cancellationToken),
        TransferState.Blocked => preflight.ResumeAsync(cursor, cancellationToken),
        TransferState.Staging => stage.RunAsync(cursor, keeper, cancellationToken),
        TransferState.Releasing => release.RunAsync(cursor, keeper, cancellationToken),
        TransferState.Publishing => publish.RunAsync(cursor, keeper, cancellationToken),
        TransferState.Reassigning => completion.RunAsync(cursor, keeper, cancellationToken),
        TransferState.Aborted => lifecycle.CleanupAsync(cursor, cancellationToken),
        _ => Task.CompletedTask,
    };

    private static async Task ClearFailureAsync(TransferJobCursor cursor, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        if (job.Attempts == 0 || TransferRules.IsFinal(job.State))
        {
            return;
        }

        job.Attempts = 0;
        job.NextAttemptAt = null;
        job.LastFailureKind = null;
        job.FailingSince = null;
        await cursor.SaveAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(Guid transferId, Exception exception, CancellationToken cancellationToken)
    {
        var stored = await jobs.GetAsync(transferId, cancellationToken);
        if (stored is null)
        {
            return;
        }

        var job = stored.Value;
        job.FailingSince ??= timeProvider.GetUtcNow();
        job.Attempts++;
        job.LastFailureKind = exception.GetType().Name;
        job.NextAttemptAt = timeProvider.GetUtcNow() + Backoff(job.Attempts);
        await jobs.ReplaceAsync(job, stored.ETag, cancellationToken);
        LogStepFailure(transferId, job.Attempts, exception.GetType().Name);
    }

    private void LogStepFailure(Guid transferId, int attempts, string exceptionType)
    {
        if (attempts >= ErrorLogAttempts)
        {
            LogStepFailedRepeatedly(logger, transferId, attempts, exceptionType);
            return;
        }

        LogStepFailed(logger, transferId, exceptionType);
    }

    private TimeSpan Backoff(int attempts)
    {
        var exponent = Math.Min(attempts - 1, MaxBackoffExponent);
        var seconds = Math.Min(BackoffBaseSeconds * Math.Pow(2, exponent), options.Value.MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task ReleaseLeaseAsync(TransferLeaseKeeper keeper, CancellationToken cancellationToken)
    {
        try
        {
            await keeper.ReleaseAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogLeaseReleaseFailed(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transfer {TransferId} step failed ({ExceptionType}); a retry is scheduled")]
    private static partial void LogStepFailed(ILogger logger, Guid transferId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transfer {TransferId} step has failed {Attempts} times in a row ({ExceptionType}); it keeps retrying")]
    private static partial void LogStepFailedRepeatedly(ILogger logger, Guid transferId, int attempts, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transfer {TransferId} was changed by another request; it is reloaded on the next run")]
    private static partial void LogJobChanged(ILogger logger, Guid transferId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The transfer job lease was lost; this run stops")]
    private static partial void LogLeaseLost(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The transfer job lease could not be released ({ExceptionType})")]
    private static partial void LogLeaseReleaseFailed(ILogger logger, string exceptionType);
}
