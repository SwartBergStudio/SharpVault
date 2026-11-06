// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.Users.Common;

public sealed partial class AdminAuditWriter(IAuditLog auditLog, TimeProvider timeProvider, ILogger<AdminAuditWriter> logger)
{
    private const string TargetIdFormat = "D";
    private const string CommittedAuditTimedOutMessage = "The audit write for a committed change timed out.";

    public Task WriteAsync(AdminAuditItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        return AppendAsync(item, logCancellation: false, cancellationToken);
    }

    public async Task WriteCommittedAsync(AdminAuditItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        using var timeout = new CancellationTokenSource(VaultLimits.CommittedAuditTimeout, timeProvider);
        try
        {
            await AppendAsync(item, logCancellation: true, timeout.Token);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(CommittedAuditTimedOutMessage, exception);
        }
    }

    private async Task AppendAsync(AdminAuditItem item, bool logCancellation, CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.AppendAsync(Build(item), cancellationToken);
        }
        catch (Exception exception) when (logCancellation || exception is not OperationCanceledException)
        {
            LogAuditFailed(logger, item.EventType, item.TransferId, exception.GetType().Name);
            throw;
        }
    }

    private AuditEntry Build(AdminAuditItem item) => new()
    {
        Id = Guid.NewGuid(),
        EventType = item.EventType,
        ActorId = item.ActorId,
        OwnerId = item.OwnerId,
        TargetId = item.TargetId?.ToString(TargetIdFormat),
        TransferId = item.TransferId,
        ReasonCategory = item.ReasonCategory,
        SecretId = item.SecretId,
        RecipientId = item.RecipientId,
        SharePermission = item.SharePermission,
        OccurredAt = timeProvider.GetUtcNow(),
        IpAddress = UserProfileText.NormalizeIpAddress(item.IpAddress),
        Reason = item.Reason,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit write failed for {EventType} on transfer {TransferId} ({ExceptionType})")]
    private static partial void LogAuditFailed(ILogger logger, AuditEventType eventType, Guid? transferId, string exceptionType);
}
