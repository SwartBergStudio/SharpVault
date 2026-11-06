// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Queries;

public sealed record GetTransferPreviewQuery(Guid SourceUserId) : IRequest<Result<TransferPreviewDto>>;

public sealed class GetTransferPreviewQueryValidator : AbstractValidator<GetTransferPreviewQuery>
{
    public GetTransferPreviewQueryValidator()
    {
        RuleFor(x => x.SourceUserId).NotEqual(Guid.Empty);
    }
}

public sealed class GetTransferPreviewQueryHandler(
    AdminActorGuard guard,
    IUserStore users,
    IVaultStore vaults,
    ITransferJobStore jobs) : IRequestHandler<GetTransferPreviewQuery, Result<TransferPreviewDto>>
{
    private const int AdminLookupLimit = 100;

    public async Task<Result<TransferPreviewDto>> Handle(GetTransferPreviewQuery request, CancellationToken cancellationToken)
    {
        var actor = await guard.RequireAsync(cancellationToken);
        if (actor is null)
        {
            return Result<TransferPreviewDto>.Forbidden();
        }

        var source = await users.GetAsync(request.SourceUserId, cancellationToken);
        if (source is null)
        {
            return Result<TransferPreviewDto>.NotFound();
        }

        if (source.Value.Id == actor.UserId)
        {
            return Result<TransferPreviewDto>.Rejected(TransferMessages.RequesterIsParty);
        }

        var inventory = await TransferInventoryReader.ReadAsync(vaults, source.Value.Id, TransferLimits.InventoryMaxPages, cancellationToken);
        var admins = await users.ListAdminsAsync(AdminLookupLimit, cancellationToken);
        var marker = await jobs.GetMarkerAsync(source.Value.Id, cancellationToken);

        return Result<TransferPreviewDto>.Success(new TransferPreviewDto
        {
            SourceUserId = source.Value.Id,
            SourceDisplayName = source.Value.DisplayName,
            SourceStatus = TransferEnumMap.ToDto(source.Value.Status),
            ItemCount = inventory.Rows.Count(row => row.Kind == VaultItemKind.ActiveSecret),
            RecycledItemCount = inventory.Rows.Count(row => row.Kind == VaultItemKind.RecycledSecret),
            FolderCount = inventory.Rows.Count(row => row.Kind == VaultItemKind.ActiveFolder),
            VersionCount = inventory.Rows.Count(row => row.Kind == VaultItemKind.Version),
            CountsAreCapped = inventory.IsCapped,
            TopLevelFolders = TopLevelFolders(inventory.Rows),
            SecondAdminAvailable = admins.Any(admin => admin.Status == UserStatus.Active
                && admin.Id != actor.UserId
                && admin.Id != source.Value.Id),
            HasOpenTransfer = marker is not null,
        });
    }

    private static List<TransferPreviewFolderDto> TopLevelFolders(IReadOnlyList<VaultInventoryRow> rows)
    {
        var secretsByFolder = rows
            .Where(row => row.Kind == VaultItemKind.ActiveSecret && row.ContainerId is not null)
            .GroupBy(row => row.ContainerId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        return rows
            .Where(row => row.Kind == VaultItemKind.ActiveFolder && row.ContainerId is null)
            .OrderBy(row => row.CreatedAt)
            .Select(row => new TransferPreviewFolderDto
            {
                FolderId = row.ItemId,
                Reference = TransferFolderReference.Of(row.ItemId),
                CreatedAt = row.CreatedAt,
                ItemCount = secretsByFolder.GetValueOrDefault(row.ItemId),
            })
            .ToList();
    }
}
