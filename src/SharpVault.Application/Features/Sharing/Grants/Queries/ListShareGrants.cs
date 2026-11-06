// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Grants.Queries;

public sealed record ListShareGrantsQuery(Guid OwnerId, ShareItemKind Kind, Guid ItemId) : IRequest<Result<ShareGrantListDto>>;

public sealed class ListShareGrantsQueryValidator : AbstractValidator<ListShareGrantsQuery>
{
    public ListShareGrantsQueryValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.ItemId).NotEqual(Guid.Empty);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public sealed partial class ListShareGrantsQueryHandler(
    ShareManagementGate gate,
    IVaultStore store,
    ICurrentUser currentUser,
    ILogger<ListShareGrantsQueryHandler> logger) : IRequestHandler<ListShareGrantsQuery, Result<ShareGrantListDto>>
{
    public async Task<Result<ShareGrantListDto>> Handle(ListShareGrantsQuery request, CancellationToken cancellationToken)
    {
        var access = await gate.RequireManageAsync(request.OwnerId, request.Kind, request.ItemId, allowRecycled: true, cancellationToken);
        if (access is null)
        {
            return Result<ShareGrantListDto>.NotFound();
        }

        var rows = await store.ListShareGrantsForItemAsync(
            request.OwnerId,
            request.Kind,
            request.ItemId,
            SharingLimits.GrantReadLimit,
            cancellationToken);

        if (rows.Count >= SharingLimits.GrantReadLimit)
        {
            LogGrantListAtLimit(logger, rows.Count);
        }

        var readable = rows.Where(row => row.Value.Permission != SharePermission.None).ToList();
        if (readable.Count < rows.Count)
        {
            LogUnreadableGrantRows(logger, rows.Count - readable.Count);
        }

        var items = readable
            .Select(row => ShareGrantDtoFactory.Create(row, ShareGrantRules.CanChange(access.Value, row.Value, currentUser.UserId)))
            .OrderBy(dto => dto.RecipientDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<ShareGrantListDto>.Success(new ShareGrantListDto { Items = items, CanGrantManage = access == SecretPermission.Owner });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Share grant list reached its read limit with {GrantCount} rows")]
    private static partial void LogGrantListAtLimit(ILogger logger, int grantCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {SkippedCount} share grant rows with an unreadable permission")]
    private static partial void LogUnreadableGrantRows(ILogger logger, int skippedCount);
}
