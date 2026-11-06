// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Transfers;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Commands;

public sealed record RejectTransferCommand(Guid TransferId, string ETag, string Reason) : IRequest<Result<TransferDto>>
{
    public override string ToString() => nameof(RejectTransferCommand);
}

public sealed class RejectTransferCommandValidator : AbstractValidator<RejectTransferCommand>
{
    public RejectTransferCommandValidator()
    {
        RuleFor(x => x.TransferId).NotEqual(Guid.Empty);
        RuleFor(x => x.ETag).ValidIfMatch();
        RuleFor(x => x.Reason).AdminReason();
    }
}

public sealed class RejectTransferCommandHandler(
    AdminActorGuard guard,
    ITransferJobStore jobs,
    AdminAuditWriter audit,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider) : IRequestHandler<RejectTransferCommand, Result<TransferDto>>
{
    public async Task<Result<TransferDto>> Handle(RejectTransferCommand request, CancellationToken cancellationToken)
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
        if (job.State != TransferState.PendingApproval || !string.Equals(stored.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<TransferDto>.Conflict();
        }

        job.State = TransferState.Rejected;
        job.DecidedBy = actor.UserId;
        job.DecidedByName = actor.DisplayName;
        job.DecidedAt = timeProvider.GetUtcNow();
        job.DecisionReason = request.Reason.Trim();

        var closed = await jobs.CloseAsync(job, stored.ETag, cancellationToken);
        if (closed == StoreWriteResult.NotFound)
        {
            return Result<TransferDto>.NotFound();
        }

        if (closed != StoreWriteResult.Written)
        {
            return Result<TransferDto>.Conflict();
        }

        await audit.WriteCommittedAsync(new AdminAuditItem(
            AuditEventType.TransferRejected,
            actor.UserId,
            job.SourceUserId,
            job.TargetUserId,
            job.Id,
            job.Category.ToString(),
            job.DecisionReason,
            requestMetadata.IpAddress));

        var closedJob = await jobs.GetAsync(job.Id, cancellationToken);
        return closedJob is null
            ? Result<TransferDto>.NotFound()
            : Result<TransferDto>.Success(TransferDtoFactory.ToDto(closedJob, actor.UserId));
    }
}
