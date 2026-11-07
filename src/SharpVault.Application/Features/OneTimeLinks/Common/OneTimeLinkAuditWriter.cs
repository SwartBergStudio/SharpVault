// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed partial class OneTimeLinkAuditWriter(IAuditLog auditLog, TimeProvider timeProvider, ILogger<OneTimeLinkAuditWriter> logger)
{
    private const string TargetIdFormat = "D";
    private const string CommittedAuditTimedOutMessage = "The audit write for a committed one-time link change timed out.";

    public Task WriteAsync(OneTimeLinkAuditItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        return AppendAsync(item, logCancellation: false, cancellationToken);
    }

    public async Task WriteCommittedAsync(OneTimeLinkAuditItem item)
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

    private async Task AppendAsync(OneTimeLinkAuditItem item, bool logCancellation, CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.AppendAsync(Build(item), cancellationToken);
        }
        catch (Exception exception) when (logCancellation || exception is not OperationCanceledException)
        {
            LogAuditFailed(logger, item.EventType, item.TargetId.ToString(TargetIdFormat), exception.GetType().Name);
            throw;
        }
    }

    private AuditEntry Build(OneTimeLinkAuditItem item) => new()
    {
        Id = Guid.NewGuid(),
        EventType = item.EventType,
        ActorId = item.ActorId,
        TargetId = item.TargetId.ToString(TargetIdFormat),
        OwnerId = item.OwnerId,
        SecretId = item.SecretId,
        OccurredAt = timeProvider.GetUtcNow(),
        IpAddress = UserProfileText.NormalizeIpAddress(item.IpAddress),
        UserAgent = UserProfileText.NormalizeUserAgent(item.UserAgent),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit write failed for one-time link event {EventType} on link {LinkId} ({ExceptionType})")]
    private static partial void LogAuditFailed(ILogger logger, AuditEventType eventType, string linkId, string exceptionType);
}
