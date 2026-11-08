// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Features.Users.Commands;

public sealed record UnlockUserCommand(Guid UserId, ReasonCategory Category, string Reason) : IRequest<Result<UserSummaryDto>>
{
    public override string ToString() => nameof(UnlockUserCommand);
}

public sealed class UnlockUserCommandValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);

        RuleFor(x => x.Category)
            .IsInEnum()
            .WithMessage(UserAdminMessages.CategoryInvalid);

        RuleFor(x => x.Reason).AdminReason();
    }
}

public sealed class UnlockUserCommandHandler(
    AdminActorGuard guard,
    UserStatusWriter statuses,
    AdminAuditWriter audit,
    IRequestMetadata requestMetadata,
    IUserStore users,
    OneTimeLinkRevoker links) : IRequestHandler<UnlockUserCommand, Result<UserSummaryDto>>
{
    public async Task<Result<UserSummaryDto>> Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        var actor = await guard.RequireAsync(cancellationToken);
        if (actor is null)
        {
            return Result<UserSummaryDto>.Forbidden();
        }

        var target = await users.GetAsync(request.UserId, cancellationToken);
        if (target?.Value.Status == UserStatus.Locked)
        {
            await links.RevokeAllForCreatorAsync(request.UserId, actor.UserId, requestMetadata.IpAddress, cancellationToken);
        }

        var unlocked = await statuses.SetStatusAsync(request.UserId, UserStatus.Active, UserAdminMessages.AlreadyActive, cancellationToken);
        if (!unlocked.IsSuccess)
        {
            return unlocked;
        }

        await audit.WriteCommittedAsync(new AdminAuditItem(
            AuditEventType.UserUnlocked,
            actor.UserId,
            OwnerId: null,
            TargetId: request.UserId,
            TransferId: null,
            ReasonCategory: request.Category.ToString(),
            Reason: request.Reason.Trim(),
            IpAddress: requestMetadata.IpAddress));

        return unlocked;
    }
}
