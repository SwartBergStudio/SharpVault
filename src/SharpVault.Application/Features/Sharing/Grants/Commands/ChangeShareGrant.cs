// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Grants.Commands;

public sealed record ChangeShareGrantCommand(
    Guid OwnerId,
    ShareItemKind Kind,
    Guid ItemId,
    Guid RecipientId,
    string ETag,
    SharePermissionDto Permission) : IRequest<Result<ShareGrantDto>>;

public sealed class ChangeShareGrantCommandValidator : AbstractValidator<ChangeShareGrantCommand>
{
    public ChangeShareGrantCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.ItemId).NotEqual(Guid.Empty);
        RuleFor(x => x.RecipientId).NotEqual(Guid.Empty);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Permission).IsInEnum();
        RuleFor(x => x.ETag).ValidIfMatch();
    }
}

public sealed class ChangeShareGrantCommandHandler(
    ShareManagementGate gate,
    IVaultStore store,
    ICurrentUser currentUser,
    VaultAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<ChangeShareGrantCommandHandler> logger) : IRequestHandler<ChangeShareGrantCommand, Result<ShareGrantDto>>
{
    public async Task<Result<ShareGrantDto>> Handle(ChangeShareGrantCommand request, CancellationToken cancellationToken)
    {
        var access = await gate.RequireManageAsync(request.OwnerId, request.Kind, request.ItemId, allowRecycled: false, cancellationToken);
        if (access is null)
        {
            return Result<ShareGrantDto>.NotFound();
        }

        var rows = await store.GetShareGrantRowsAsync(request.OwnerId, request.Kind, request.ItemId, request.RecipientId, cancellationToken);
        if (rows is null || !ShareGrantRules.CanChange(access.Value, rows.ByItem.Value, currentUser.UserId))
        {
            return Result<ShareGrantDto>.NotFound();
        }

        if (!ShareGrantRules.CanGrant(access.Value, request.Permission))
        {
            return Result<ShareGrantDto>.Rejected(SharingMessages.OnlyOwnerGrantsManage);
        }

        if (!string.Equals(rows.ByItem.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<ShareGrantDto>.Conflict();
        }

        var grant = rows.ByItem.Value;
        grant.Permission = SharingEnumMap.ToDomain(request.Permission);
        grant.ModifiedAt = timeProvider.GetUtcNow();

        var change = VaultChange.ReplaceShareGrant(grant, rows.ByItem.ETag, rows.ByRecipientETag);
        var write = await store.SubmitAsync(request.OwnerId, [change], cancellationToken);
        if (VaultWrites.ToFailure<ShareGrantDto>(write, logger, request.ItemId) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(ShareAuditItems.Changed(grant));

        var dto = ShareGrantDtoFactory.Create(new Versioned<ShareGrant>(grant, VaultWrites.ETagAt(write, 0)), canChange: true);
        return Result<ShareGrantDto>.Success(dto);
    }
}
