// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.SharedItems.Queries;

public sealed record GetSharedFolderQuery(Guid OwnerId, Guid FolderId, int PageSize, string? ContinuationToken)
    : IRequest<Result<SharedFolderContentsDto>>;

public sealed class GetSharedFolderQueryValidator : AbstractValidator<GetSharedFolderQuery>
{
    public GetSharedFolderQueryValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.FolderId).NotEqual(Guid.Empty);
        RuleFor(x => x.PageSize).InclusiveBetween(1, SharingLimits.MaxPageSize);
        RuleFor(x => x.ContinuationToken)
            .Must(ContinuationTokens.IsSecretToken)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(SharingMessages.InvalidContinuationToken);
    }
}

public sealed class GetSharedFolderQueryHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<GetSharedFolderQuery, Result<SharedFolderContentsDto>>
{
    public async Task<Result<SharedFolderContentsDto>> Handle(GetSharedFolderQuery request, CancellationToken cancellationToken)
    {
        if (request.OwnerId == currentUser.UserId)
        {
            return Result<SharedFolderContentsDto>.NotFound();
        }

        var grants = await store.ListFolderGrantsToRecipientAsync(request.OwnerId, currentUser.UserId, cancellationToken);
        if (grants.Count == 0)
        {
            return Result<SharedFolderContentsDto>.NotFound();
        }

        var foldersTask = store.ListFoldersAsync(request.OwnerId, cancellationToken);
        var readTask = keyRings.OpenForReadAsync(request.OwnerId, cancellationToken);
        var ownerTask = users.GetAsync(request.OwnerId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(readTask, foldersTask, ownerTask);

        using var read = await readTask;
        var folders = await foldersTask;
        var access = ResolveAccess(request.FolderId, folders, grants);
        if (access is null)
        {
            return Result<SharedFolderContentsDto>.NotFound();
        }

        SecretPage page;
        try
        {
            page = await store.ListSecretsAsync(
                request.OwnerId,
                SecretFolderFilter.InFolder(request.FolderId),
                request.PageSize,
                request.ContinuationToken,
                cancellationToken);
        }
        catch (InvalidContinuationTokenException)
        {
            return Result<SharedFolderContentsDto>.Rejected(SharingMessages.InvalidContinuationToken);
        }

        var owner = await ownerTask;
        var folder = SummaryOf(access.Chain[0], read.Vault);
        return Result<SharedFolderContentsDto>.Success(new SharedFolderContentsDto
        {
            FolderId = folder.FolderId,
            Name = folder.Name,
            IsDamaged = folder.IsDamaged,
            OwnerId = request.OwnerId,
            OwnerDisplayName = owner?.Value.DisplayName ?? SharingMessages.UnknownOwner,
            Permission = SharedPermissionMap.ToDto(access.Permission),
            Path = [.. access.Chain.Take(access.TopGrantedIndex + 1).Reverse().Select(row => SummaryOf(row, read.Vault))],
            SubFolders = SubFoldersOf(request.FolderId, folders, read.Vault),
            Items = [.. page.Items.Select(item => SecretDtoFactory.ForRecipient(item, read.Vault))],
            ContinuationToken = page.ContinuationToken,
        });
    }

    private static FolderAccess? ResolveAccess(Guid folderId, IReadOnlyList<Versioned<Folder>> folders, IReadOnlyList<ShareGrant> grants)
    {
        var byId = folders.ToDictionary(row => row.Value.Id, row => row.Value);
        var grantedByFolder = new Dictionary<Guid, SecretPermission>(grants.Count);
        foreach (var grant in grants)
        {
            var permission = SharingEnumMap.ToAccess(grant.Permission);
            if (!grantedByFolder.TryGetValue(grant.ItemId, out var existing) || permission > existing)
            {
                grantedByFolder[grant.ItemId] = permission;
            }
        }

        var chain = new List<Folder>();
        var current = (Guid?)folderId;
        for (var depth = 0; depth < VaultLimits.MaxFolderDepth && current is { } id && byId.TryGetValue(id, out var row); depth++)
        {
            chain.Add(row);
            current = row.ParentId;
        }

        var best = SecretPermission.None;
        var topGranted = -1;
        for (var index = 0; index < chain.Count; index++)
        {
            if (grantedByFolder.TryGetValue(chain[index].Id, out var granted) && granted >= SecretPermission.View)
            {
                best = granted > best ? granted : best;
                topGranted = index;
            }
        }

        return chain.Count == 0 || topGranted < 0 ? null : new FolderAccess(chain, best, topGranted);
    }

    private static List<SharedFolderSummaryDto> SubFoldersOf(Guid folderId, IReadOnlyList<Versioned<Folder>> folders, OpenedVault? vault) =>
    [
        .. folders
            .Where(row => row.Value.ParentId == folderId)
            .Select(row => SummaryOf(row.Value, vault))
            .OrderBy(summary => summary.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase),
    ];

    private static SharedFolderSummaryDto SummaryOf(Folder row, OpenedVault? vault)
    {
        var summary = new SharedFolderSummaryDto { FolderId = row.Id };
        if (vault is not null && SecretMetadataCipher.TryDecryptFolderName(vault.Session, row, vault.MetadataKeyVersion, out var name))
        {
            summary.Name = name;
            return summary;
        }

        summary.IsDamaged = true;
        return summary;
    }

    private sealed record FolderAccess(List<Folder> Chain, SecretPermission Permission, int TopGrantedIndex);
}
