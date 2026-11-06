// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Grants.Commands;

public sealed record CreateShareGrantCommand(
    Guid OwnerId,
    ShareItemKind Kind,
    Guid ItemId,
    Guid RecipientId,
    SharePermissionDto Permission) : IRequest<Result<ShareGrantDto>>;

public sealed class CreateShareGrantCommandValidator : AbstractValidator<CreateShareGrantCommand>
{
    public CreateShareGrantCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.ItemId).NotEqual(Guid.Empty);
        RuleFor(x => x.RecipientId).NotEqual(Guid.Empty);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Permission).IsInEnum();
    }
}

public sealed class CreateShareGrantCommandHandler(
    ShareManagementGate gate,
    ShareGrantService grants,
    IVaultStore store,
    IUserDirectory directory,
    IUserStore users,
    ICurrentUser currentUser,
    VaultAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<CreateShareGrantCommandHandler> logger) : IRequestHandler<CreateShareGrantCommand, Result<ShareGrantDto>>
{
    public async Task<Result<ShareGrantDto>> Handle(CreateShareGrantCommand request, CancellationToken cancellationToken)
    {
        var access = await gate.RequireManageAsync(request.OwnerId, request.Kind, request.ItemId, allowRecycled: false, cancellationToken);
        if (access is null)
        {
            return Result<ShareGrantDto>.NotFound();
        }

        if (!ShareGrantRules.CanGrant(access.Value, request.Permission))
        {
            return Result<ShareGrantDto>.Rejected(SharingMessages.OnlyOwnerGrantsManage);
        }

        if (request.RecipientId == request.OwnerId || request.RecipientId == currentUser.UserId)
        {
            return Result<ShareGrantDto>.Rejected(SharingMessages.RecipientUnavailable);
        }

        var member = await directory.FindMemberAsync(request.RecipientId, cancellationToken);
        if (member is null)
        {
            return Result<ShareGrantDto>.Rejected(SharingMessages.RecipientUnavailable);
        }

        var recipientRow = await users.GetAsync(request.RecipientId, cancellationToken);
        if (recipientRow is { Value.Status: not UserStatus.Active })
        {
            return Result<ShareGrantDto>.Rejected(SharingMessages.RecipientUnavailable);
        }

        var existing = await store.ListShareGrantsForItemAsync(
            request.OwnerId,
            request.Kind,
            request.ItemId,
            SharingLimits.MaxGrantsPerItem + 1,
            cancellationToken);
        if (existing.Any(row => row.Value.RecipientId == request.RecipientId))
        {
            return Result<ShareGrantDto>.Conflict();
        }

        if (existing.Count >= SharingLimits.MaxGrantsPerItem)
        {
            return Result<ShareGrantDto>.Rejected(SharingMessages.TooManyRecipients);
        }

        var now = timeProvider.GetUtcNow();
        var grant = new ShareGrant
        {
            OwnerId = request.OwnerId,
            ItemKind = request.Kind,
            ItemId = request.ItemId,
            RecipientId = request.RecipientId,
            Permission = SharingEnumMap.ToDomain(request.Permission),
            RecipientDisplayName = UserProfileText.NormalizeDisplayName(member.DisplayName),
            RecipientEmail = UserProfileText.NormalizeEmail(member.Email),
            GrantedById = currentUser.UserId,
            GrantedByDisplayName = UserProfileText.NormalizeDisplayName(currentUser.DisplayName),
            GrantedAt = now,
            ModifiedAt = now,
        };

        var write = await grants.AddAsync(grant, filedFolderId: null, cancellationToken);
        if (VaultWrites.ToFailure<ShareGrantDto>(write, logger, request.ItemId) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(ShareAuditItems.Shared(grant));

        var dto = ShareGrantDtoFactory.Create(new Versioned<ShareGrant>(grant, VaultWrites.ETagAt(write, 0)), canChange: true);
        return Result<ShareGrantDto>.Success(dto);
    }
}
