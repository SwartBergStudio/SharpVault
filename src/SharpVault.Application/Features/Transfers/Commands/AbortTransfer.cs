// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Transfers;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Commands;

public sealed record AbortTransferCommand(Guid TransferId, string ETag, string Reason) : IRequest<Result<TransferDto>>
{
    public override string ToString() => nameof(AbortTransferCommand);
}

public sealed class AbortTransferCommandValidator : AbstractValidator<AbortTransferCommand>
{
    public AbortTransferCommandValidator()
    {
        RuleFor(x => x.TransferId).NotEqual(Guid.Empty);
        RuleFor(x => x.ETag).ValidIfMatch();
        RuleFor(x => x.Reason).AdminReason();
    }
}

public sealed class AbortTransferCommandHandler(
    AdminActorGuard guard,
    ITransferJobStore jobs,
    AdminAuditWriter audit,
    IRequestMetadata requestMetadata,
    ITransferJobSignal signal) : IRequestHandler<AbortTransferCommand, Result<TransferDto>>
{
    public async Task<Result<TransferDto>> Handle(AbortTransferCommand request, CancellationToken cancellationToken)
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
        if (!TransferRules.CanAbort(job.State) || !string.Equals(stored.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<TransferDto>.Conflict();
        }

        job.State = TransferState.Aborted;
        job.CleanupPending = true;
        job.DecisionReason = request.Reason.Trim();

        var replaced = await jobs.ReplaceAsync(job, stored.ETag, cancellationToken);
        if (replaced is null)
        {
            return Result<TransferDto>.Conflict();
        }

        await audit.WriteCommittedAsync(new AdminAuditItem(
            AuditEventType.TransferAborted,
            actor.UserId,
            job.SourceUserId,
            job.TargetUserId,
            job.Id,
            job.Category.ToString(),
            job.DecisionReason,
            requestMetadata.IpAddress));

        signal.Signal();
        return Result<TransferDto>.Success(TransferDtoFactory.ToDto(replaced, actor.UserId));
    }
}
