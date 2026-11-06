// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.Vault.Common;

public sealed partial class VaultAuditWriter(
    ICurrentUser currentUser,
    IRequestMetadata requestMetadata,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<VaultAuditWriter> logger)
{
    private const string TargetIdFormat = "D";
    private const string CommittedAuditTimedOutMessage = "The audit write for a committed change timed out.";

    public Task WriteAsync(VaultAuditItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        return AppendAsync(item, logCancellation: false, cancellationToken);
    }

    public Task WriteManyAsync(IReadOnlyList<VaultAuditItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        return AppendEveryAsync(items, logCancellation: false, cancellationToken);
    }

    public Task WriteCommittedAsync(VaultAuditItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return WriteCommittedManyAsync([item]);
    }

    public async Task WriteCommittedManyAsync(IReadOnlyList<VaultAuditItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(VaultLimits.CommittedAuditTimeout, timeProvider);
        try
        {
            await AppendEveryAsync(items, logCancellation: true, timeout.Token);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(CommittedAuditTimedOutMessage, exception);
        }
    }

    private async Task AppendEveryAsync(IReadOnlyList<VaultAuditItem> items, bool logCancellation, CancellationToken cancellationToken)
    {
        var failures = new ConcurrentQueue<Exception>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = VaultLimits.AuditParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(items, options, async (item, token) =>
        {
            try
            {
                await AppendAsync(item, logCancellation, token);
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        if (failures.IsEmpty)
        {
            return;
        }

        var primary = failures.FirstOrDefault(failure => failure is not OperationCanceledException) ?? failures.First();
        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private async Task AppendAsync(VaultAuditItem item, bool logCancellation, CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.AppendAsync(Build(item), cancellationToken);
        }
        catch (Exception exception) when (logCancellation || exception is not OperationCanceledException)
        {
            LogAuditFailed(logger, item.EventType, item.TargetId, exception.GetType().Name);
            throw;
        }
    }

    private AuditEntry Build(VaultAuditItem item) => new()
    {
        Id = Guid.NewGuid(),
        EventType = item.EventType,
        ActorId = currentUser.UserId,
        OwnerId = item.OwnerId,
        TargetId = item.TargetId.ToString(TargetIdFormat),
        SecretId = item.SecretId,
        RecipientId = item.RecipientId,
        SharePermission = item.SharePermission,
        OccurredAt = timeProvider.GetUtcNow(),
        IpAddress = requestMetadata.IpAddress,
        Reason = null,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit write failed for {EventType} on {ItemId} ({ExceptionType})")]
    private static partial void LogAuditFailed(ILogger logger, AuditEventType eventType, Guid itemId, string exceptionType);
}
