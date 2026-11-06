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

namespace SharpVault.Application.Features.Sharing.Grants.Commands;

public sealed record RevokeShareGrantCommand(
    Guid OwnerId,
    ShareItemKind Kind,
    Guid ItemId,
    Guid RecipientId) : IRequest<Result<Done>>;

public sealed class RevokeShareGrantCommandValidator : AbstractValidator<RevokeShareGrantCommand>
{
    public RevokeShareGrantCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.ItemId).NotEqual(Guid.Empty);
        RuleFor(x => x.RecipientId).NotEqual(Guid.Empty);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public sealed partial class RevokeShareGrantCommandHandler(
    ShareManagementGate gate,
    ShareGrantService grants,
    IVaultStore store,
    ICurrentUser currentUser,
    VaultAuditWriter audit,
    ILogger<RevokeShareGrantCommandHandler> logger) : IRequestHandler<RevokeShareGrantCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(RevokeShareGrantCommand request, CancellationToken cancellationToken)
    {
        var access = await gate.RequireManageAsync(request.OwnerId, request.Kind, request.ItemId, allowRecycled: true, cancellationToken);
        if (access is null)
        {
            return Result<Done>.NotFound();
        }

        if (access.Value != SecretPermission.Owner)
        {
            var existing = await store.GetShareGrantAsync(request.OwnerId, request.Kind, request.ItemId, request.RecipientId, cancellationToken);
            if (existing is null || !ShareGrantRules.CanChange(access.Value, existing.Value, currentUser.UserId))
            {
                return Result<Done>.NotFound();
            }
        }

        var outcome = await grants.RevokeAsync(request.OwnerId, request.Kind, request.ItemId, request.RecipientId, cancellationToken);
        switch (outcome.Result)
        {
            case ShareRevokeResult.Revoked when outcome.Revoked is { } revoked:
                await audit.WriteCommittedAsync(ShareAuditItems.Revoked(revoked));
                return Result<Done>.Success(Done.Value);
            case ShareRevokeResult.Conflict:
                LogRevokeConflict(logger, request.ItemId);
                return Result<Done>.Conflict();
            default:
                return Result<Done>.NotFound();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Share revoke conflict on {ItemId}")]
    private static partial void LogRevokeConflict(ILogger logger, Guid itemId);
}
