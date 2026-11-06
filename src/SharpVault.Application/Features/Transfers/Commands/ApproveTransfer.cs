// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Commands;

public sealed record ApproveTransferCommand(Guid TransferId, string ETag) : IRequest<Result<TransferDto>>;

public sealed class ApproveTransferCommandValidator : AbstractValidator<ApproveTransferCommand>
{
    public ApproveTransferCommandValidator()
    {
        RuleFor(x => x.TransferId).NotEqual(Guid.Empty);
        RuleFor(x => x.ETag).ValidIfMatch();
    }
}

public sealed partial class ApproveTransferCommandHandler(
    AdminActorGuard guard,
    ITransferJobStore jobs,
    IUserStore users,
    TransferAdmission admission,
    AdminAuditWriter audit,
    IRequestMetadata requestMetadata,
    ITransferJobSignal signal,
    TimeProvider timeProvider,
    ILogger<ApproveTransferCommandHandler> logger) : IRequestHandler<ApproveTransferCommand, Result<TransferDto>>
{
    public async Task<Result<TransferDto>> Handle(ApproveTransferCommand request, CancellationToken cancellationToken)
    {
        var actor = await guard.RequireAsync(cancellationToken);
        if (actor is null)
        {
            return Result<TransferDto>.Forbidden();
        }

        var stored = await jobs.GetAsync(request.TransferId, cancellationToken);
        if (stored is null || TransferVisibility.IsHiddenFrom(stored.Value, actor.UserId))
        {
            return Result<TransferDto>.NotFound();
        }

        var job = stored.Value;
        if (job.RequestedBy == actor.UserId)
        {
            await RecordDenialAsync(job, actor.UserId, cancellationToken);
            return Result<TransferDto>.Forbidden();
        }

        if (job.State != TransferState.PendingApproval || !string.Equals(stored.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<TransferDto>.Conflict();
        }

        var now = timeProvider.GetUtcNow();
        if (now >= job.ExpiresAt)
        {
            return Result<TransferDto>.Rejected(TransferMessages.RequestExpired);
        }

        var problem = await CheckPartiesAsync(job, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var selectionProblem = (await admission.CheckSelectionAsync(job.SourceUserId, job.TargetUserId, job.ScopeKind, job.SelectedFolderIds, cancellationToken)).Problem;
        if (selectionProblem is not null)
        {
            return Result<TransferDto>.Rejected(selectionProblem);
        }

        job.State = TransferState.Approved;
        job.DecidedBy = actor.UserId;
        job.DecidedByName = actor.DisplayName;
        job.DecidedAt = now;
        job.ApprovedFromIp = UserProfileText.NormalizeIpAddress(requestMetadata.IpAddress);
        job.ApprovalAudited = false;

        var replaced = await jobs.ReplaceAsync(job, stored.ETag, cancellationToken);
        if (replaced is null)
        {
            return Result<TransferDto>.Conflict();
        }

        signal.Signal();
        return Result<TransferDto>.Success(TransferDtoFactory.ToDto(replaced, actor.UserId));
    }

    private async Task<Result<TransferDto>?> CheckPartiesAsync(TransferJob job, CancellationToken cancellationToken)
    {
        var requester = await users.GetAsync(job.RequestedBy, cancellationToken);
        if (requester is not { Value: { Status: UserStatus.Active, IsAdmin: true } })
        {
            return Result<TransferDto>.Rejected(TransferMessages.RequesterNoLongerAdmin);
        }

        var source = await users.GetAsync(job.SourceUserId, cancellationToken);
        if (source is null)
        {
            return Result<TransferDto>.NotFound();
        }

        if (TransferRules.RequiresLockedSource(job.Category) && source.Value.Status != UserStatus.Locked)
        {
            return Result<TransferDto>.Rejected(TransferMessages.SourceMustBeLocked);
        }

        var target = await users.GetAsync(job.TargetUserId, cancellationToken);
        if (target is null)
        {
            return Result<TransferDto>.NotFound();
        }

        return target.Value.Status == UserStatus.Active
            ? null
            : Result<TransferDto>.Rejected(TransferMessages.TargetLocked);
    }

    private async Task RecordDenialAsync(TransferJob job, Guid actorId, CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync(new AdminAuditItem(
                AuditEventType.TransferApprovalDenied,
                actorId,
                job.SourceUserId,
                job.TargetUserId,
                job.Id,
                job.Category.ToString(),
                null,
                requestMetadata.IpAddress), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDenialNotAudited(logger, job.Id, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The approval denial for transfer {TransferId} could not be audited ({ExceptionType})")]
    private static partial void LogDenialNotAudited(ILogger logger, Guid transferId, string exceptionType);
}
