// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mapster;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Features.Users.Commands;

public sealed record LockUserCommand(Guid UserId, ReasonCategory Category, string Reason) : IRequest<Result<UserSummaryDto>>
{
    public override string ToString() => nameof(LockUserCommand);
}

public sealed class LockUserCommandValidator : AbstractValidator<LockUserCommand>
{
    public LockUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);

        RuleFor(x => x.Category)
            .IsInEnum()
            .WithMessage(UserAdminMessages.CategoryInvalid);

        RuleFor(x => x.Reason).AdminReason();
    }
}

public sealed class LockUserCommandHandler(
    AdminActorGuard guard,
    UserStatusWriter statuses,
    AdminAuditWriter audit,
    VaultAuditWriter vaultAudit,
    ShareGrantService shares,
    IUserStore users,
    IRequestMetadata requestMetadata) : IRequestHandler<LockUserCommand, Result<UserSummaryDto>>
{
    public async Task<Result<UserSummaryDto>> Handle(LockUserCommand request, CancellationToken cancellationToken)
    {
        var actor = await guard.RequireAsync(cancellationToken);
        if (actor is null)
        {
            return Result<UserSummaryDto>.Forbidden();
        }

        if (request.UserId == actor.UserId)
        {
            return Result<UserSummaryDto>.Rejected(UserAdminMessages.CannotLockSelf);
        }

        var locked = await statuses.SetStatusAsync(request.UserId, UserStatus.Locked, UserAdminMessages.AlreadyLocked, cancellationToken);
        if (!locked.IsSuccess && !IsRepeatDismissal(request, locked))
        {
            return locked;
        }

        await audit.WriteCommittedAsync(new AdminAuditItem(
            AuditEventType.UserLocked,
            actor.UserId,
            OwnerId: null,
            TargetId: request.UserId,
            TransferId: null,
            ReasonCategory: request.Category.ToString(),
            Reason: request.Reason.Trim(),
            IpAddress: requestMetadata.IpAddress));

        if (request.Category == ReasonCategory.Dismissed)
        {
            await RevokeReceivedSharesAsync(request.UserId, cancellationToken);
        }

        return locked.IsSuccess ? locked : await SummaryAsync(request.UserId, cancellationToken);
    }

    private static bool IsRepeatDismissal(LockUserCommand request, Result<UserSummaryDto> locked) =>
        request.Category == ReasonCategory.Dismissed && locked.Error == UserAdminMessages.AlreadyLocked;

    private async Task<Result<UserSummaryDto>> SummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var stored = await users.GetAsync(userId, cancellationToken);
        return stored is null
            ? Result<UserSummaryDto>.NotFound()
            : Result<UserSummaryDto>.Success(stored.Value.Adapt<UserSummaryDto>());
    }

    private async Task RevokeReceivedSharesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var outcome = await shares.RevokeAllReceivedAsync(userId, AuditRevokedAsync, cancellationToken);
        if (!outcome.IsComplete)
        {
            throw new InvalidOperationException(UserAdminMessages.ReceivedSharesNotRevoked);
        }
    }

    private Task AuditRevokedAsync(ShareGrant grant) => vaultAudit.WriteCommittedAsync(ToRevokedAudit(grant));

    private static VaultAuditItem ToRevokedAudit(ShareGrant grant) => grant.ItemKind == ShareItemKind.Secret
        ? new VaultAuditItem(AuditEventType.SecretShareRevoked, grant.OwnerId, grant.ItemId, grant.ItemId, grant.RecipientId)
        : new VaultAuditItem(AuditEventType.FolderShareRevoked, grant.OwnerId, grant.ItemId, null, grant.RecipientId);
}
