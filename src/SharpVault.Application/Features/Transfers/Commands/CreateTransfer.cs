// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Commands;

public sealed record CreateTransferCommand(
    Guid SourceUserId,
    Guid TargetUserId,
    TransferScopeKind Scope,
    IReadOnlyList<Guid>? FolderIds,
    ReasonCategory Category,
    string Reason) : IRequest<Result<TransferDto>>
{
    public override string ToString() => nameof(CreateTransferCommand);
}

public sealed class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(x => x.SourceUserId).NotEqual(Guid.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(Guid.Empty);

        RuleFor(x => x.Scope).IsInEnum();

        RuleFor(x => x)
            .Must(x => TransferFolderSelection.IsValid(x.Scope, x.FolderIds))
            .WithName(nameof(CreateTransferCommand.FolderIds))
            .WithMessage(TransferMessages.FolderSelectionInvalid);

        RuleFor(x => x.Category)
            .IsInEnum()
            .WithMessage(UserAdminMessages.CategoryInvalid);

        RuleFor(x => x.Reason).AdminReason();
    }
}

public sealed class CreateTransferCommandHandler(
    AdminActorGuard guard,
    IUserStore users,
    ITransferJobStore jobs,
    TransferAdmission admission,
    AdminAuditWriter audit,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider,
    IOptions<TransferOptions> options) : IRequestHandler<CreateTransferCommand, Result<TransferDto>>
{
    private const int AdminLookupLimit = 100;

    public async Task<Result<TransferDto>> Handle(CreateTransferCommand request, CancellationToken cancellationToken)
    {
        var actor = await guard.RequireAsync(cancellationToken);
        if (actor is null)
        {
            return Result<TransferDto>.Forbidden();
        }

        if (actor.UserId == request.SourceUserId || actor.UserId == request.TargetUserId)
        {
            return Result<TransferDto>.Rejected(TransferMessages.RequesterIsParty);
        }

        if (request.SourceUserId == request.TargetUserId)
        {
            return Result<TransferDto>.Rejected(TransferMessages.SourceIsTarget);
        }

        var source = await users.GetAsync(request.SourceUserId, cancellationToken);
        var target = await users.GetAsync(request.TargetUserId, cancellationToken);
        if (source is null || target is null)
        {
            return Result<TransferDto>.NotFound();
        }

        if (TransferRules.RequiresLockedSource(request.Category) && source.Value.Status != UserStatus.Locked)
        {
            return Result<TransferDto>.Rejected(TransferMessages.SourceMustBeLocked);
        }

        if (target.Value.Status != UserStatus.Active)
        {
            return Result<TransferDto>.Rejected(TransferMessages.TargetLocked);
        }

        if (!await HasSecondAdminAsync(actor.UserId, request.SourceUserId, request.TargetUserId, cancellationToken))
        {
            return Result<TransferDto>.Rejected(TransferMessages.SecondAdminRequired);
        }

        var folderIds = TransferFolderSelection.Normalize(request.Scope, request.FolderIds);
        var selection = await admission.CheckSelectionAsync(request.SourceUserId, request.TargetUserId, request.Scope, folderIds, cancellationToken);
        if (selection.Problem is { } selectionProblem)
        {
            return Result<TransferDto>.Rejected(selectionProblem);
        }

        if (await IsChainAsync(request.SourceUserId, request.TargetUserId, cancellationToken))
        {
            return Result<TransferDto>.Rejected(TransferMessages.TransferChainNotAllowed);
        }

        var job = BuildJob(request, actor, source.Value, target.Value, folderIds);
        selection.Scope.ApplyCountsTo(job);
        var added = await jobs.AddWithMarkerAsync(job, cancellationToken);
        if (added == StoreWriteResult.AlreadyExists)
        {
            return Result<TransferDto>.Rejected(TransferMessages.OpenTransferExists);
        }

        if (added != StoreWriteResult.Written)
        {
            return Result<TransferDto>.Conflict();
        }

        await audit.WriteCommittedAsync(new AdminAuditItem(
            AuditEventType.TransferRequested,
            actor.UserId,
            job.SourceUserId,
            job.TargetUserId,
            job.Id,
            job.Category.ToString(),
            job.Reason,
            requestMetadata.IpAddress));

        var stored = await jobs.GetAsync(job.Id, cancellationToken);
        return stored is null
            ? Result<TransferDto>.Conflict()
            : Result<TransferDto>.Success(TransferDtoFactory.ToDto(stored, actor.UserId));
    }

    private async Task<bool> HasSecondAdminAsync(Guid requesterId, Guid sourceId, Guid targetId, CancellationToken cancellationToken)
    {
        var admins = await users.ListAdminsAsync(AdminLookupLimit, cancellationToken);
        return admins.Any(admin => admin.Status == UserStatus.Active
            && admin.Id != requesterId
            && admin.Id != sourceId
            && admin.Id != targetId);
    }

    private async Task<bool> IsChainAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken)
    {
        var markers = await jobs.ListActiveAsync(cancellationToken);
        return markers.Any(marker => marker.SourceUserId == targetId || marker.TargetUserId == sourceId);
    }

    private TransferJob BuildJob(
        CreateTransferCommand request,
        AdminActor actor,
        User source,
        User target,
        IReadOnlyList<Guid> folderIds)
    {
        var now = timeProvider.GetUtcNow();
        var id = VaultIds.NewId(timeProvider);

        return new TransferJob
        {
            Id = id,
            State = TransferState.PendingApproval,
            SourceUserId = source.Id,
            SourceDisplayName = source.DisplayName,
            TargetUserId = target.Id,
            TargetDisplayName = target.DisplayName,
            ScopeKind = request.Scope,
            SelectedFolderIds = folderIds,
            Category = request.Category,
            Reason = request.Reason.Trim(),
            RequestedBy = actor.UserId,
            RequestedByName = actor.DisplayName,
            RequestedAt = now,
            ExpiresAt = now + options.Value.RequestLifetime,
            RootFolderId = VaultIds.TransferRootFolderIdFor(id),
        };
    }
}
