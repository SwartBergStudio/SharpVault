// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
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
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Sharing.SharedItems.Queries;

public sealed record ListSharedItemsQuery(Guid? FiledFolderId, int PageSize, string? ContinuationToken)
    : IRequest<Result<SharedItemPageDto>>
{
    public const int DefaultPageSize = SharingLimits.DefaultPageSize;

    public const int MaxPageSize = SharingLimits.MaxPageSize;
}

public sealed class ListSharedItemsQueryValidator : AbstractValidator<ListSharedItemsQuery>
{
    public ListSharedItemsQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListSharedItemsQuery.MaxPageSize);
        RuleFor(x => x.FiledFolderId).NotEqual(Guid.Empty).When(x => x.FiledFolderId is not null);
        RuleFor(x => x.ContinuationToken)
            .Must(ContinuationTokens.IsInboxToken)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(SharingMessages.InvalidContinuationToken);
    }
}

public sealed class ListSharedItemsQueryHandler(
    IShareInboxStore inbox,
    IVaultStore store,
    VaultKeyRingService keyRings,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<ListSharedItemsQuery, Result<SharedItemPageDto>>
{
    public async Task<Result<SharedItemPageDto>> Handle(ListSharedItemsQuery request, CancellationToken cancellationToken)
    {
        ShareInboxPage page;
        try
        {
            page = await inbox.ListAsync(currentUser.UserId, request.FiledFolderId, request.PageSize, request.ContinuationToken, cancellationToken);
        }
        catch (InvalidContinuationTokenException)
        {
            return Result<SharedItemPageDto>.Rejected(SharingMessages.InvalidContinuationToken);
        }

        var entries = page.Items.Where(row => row.Value.OwnerId != currentUser.UserId).Select(row => row.Value).ToList();
        var owners = await OpenOwnersAsync(entries, cancellationToken);
        try
        {
            var granted = entries.Where(entry => owners[entry.OwnerId].Grants.ContainsKey((entry.ItemKind, entry.ItemId))).ToList();
            var rows = await ReadItemsAsync(granted, cancellationToken);
            var items = new List<SharedItemDto>(granted.Count);
            for (var index = 0; index < granted.Count; index++)
            {
                if (rows[index] is { } row)
                {
                    items.Add(ToDto(granted[index], row, owners[granted[index].OwnerId]));
                }
            }

            return Result<SharedItemPageDto>.Success(new SharedItemPageDto { Items = items, ContinuationToken = page.ContinuationToken });
        }
        finally
        {
            DisposeAll(owners.Values);
        }
    }

    private static SharedItemDto ToDto(ShareInboxEntry entry, ItemRow row, OwnerContext owner)
    {
        var grant = owner.Grants[(entry.ItemKind, entry.ItemId)];
        var dto = new SharedItemDto
        {
            Kind = SharingEnumMap.ToDto(entry.ItemKind),
            ItemId = entry.ItemId,
            OwnerId = entry.OwnerId,
            OwnerDisplayName = owner.DisplayName,
            Permission = SharingEnumMap.ToDto(grant.Permission),
            SharedAt = entry.SharedAt,
            FiledFolderId = entry.FiledFolderId,
        };

        var vault = owner.Read.Vault;
        if (row.Secret is { } secret)
        {
            dto.Type = VaultEnumMap.ToDto(secret.Type);
            if (vault is not null && SecretMetadataCipher.TryDecrypt(vault.Session, secret, vault.MetadataKeyVersion, out var metadata))
            {
                dto.Name = metadata.Name;
                dto.Username = metadata.Username;
                return dto;
            }
        }
        else if (row.Folder is { } folder
            && vault is not null
            && SecretMetadataCipher.TryDecryptFolderName(vault.Session, folder, vault.MetadataKeyVersion, out var name))
        {
            dto.Name = name;
            return dto;
        }

        dto.IsDamaged = true;
        return dto;
    }

    private static void DisposeAll(IEnumerable<OwnerContext> owners)
    {
        foreach (var owner in owners)
        {
            owner.Read.Dispose();
        }
    }

    private static (ShareItemRef First, ShareItemRef Last) RangeOf(IEnumerable<ShareInboxEntry> entries)
    {
        var ordered = entries
            .Select(entry => new ShareItemRef(entry.ItemKind, entry.ItemId))
            .OrderBy(item => item.Kind == ShareItemKind.Folder ? 0 : 1)
            .ThenBy(item => item.ItemId.ToString("N"), StringComparer.Ordinal)
            .ToList();
        return (ordered[0], ordered[^1]);
    }

    private async Task<Dictionary<Guid, OwnerContext>> OpenOwnersAsync(List<ShareInboxEntry> entries, CancellationToken cancellationToken)
    {
        var opened = new ConcurrentDictionary<Guid, OwnerContext>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = VaultLimits.FolderQueryParallelism, CancellationToken = cancellationToken };
        try
        {
            await Parallel.ForEachAsync(entries.GroupBy(entry => entry.OwnerId), options, async (group, token) =>
            {
                opened[group.Key] = await OpenOwnerAsync(group.Key, RangeOf(group), token);
            });
        }
        catch
        {
            DisposeAll(opened.Values);
            throw;
        }

        return new Dictionary<Guid, OwnerContext>(opened);
    }

    private async Task<OwnerContext> OpenOwnerAsync(Guid ownerId, (ShareItemRef First, ShareItemRef Last) range, CancellationToken cancellationToken)
    {
        var grantsTask = store.ListGrantsToRecipientBetweenAsync(ownerId, currentUser.UserId, range.First, range.Last, cancellationToken);
        var readTask = keyRings.OpenForReadAsync(ownerId, cancellationToken);
        var ownerTask = users.GetAsync(ownerId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(readTask, grantsTask, ownerTask);

        var grants = new Dictionary<(ShareItemKind, Guid), ShareGrant>();
        foreach (var grant in await grantsTask)
        {
            if (grant.RecipientId == currentUser.UserId && grant.Permission != SharePermission.None)
            {
                grants[(grant.ItemKind, grant.ItemId)] = grant;
            }
        }

        var owner = await ownerTask;
        return new OwnerContext(await readTask, grants, owner?.Value.DisplayName ?? SharingMessages.UnknownOwner);
    }

    private async Task<ItemRow?[]> ReadItemsAsync(List<ShareInboxEntry> entries, CancellationToken cancellationToken)
    {
        var rows = new ItemRow?[entries.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = SharingLimits.ItemReadParallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Count), options, async (index, token) =>
        {
            rows[index] = await ReadItemAsync(entries[index], token);
        });

        return rows;
    }

    private async Task<ItemRow?> ReadItemAsync(ShareInboxEntry entry, CancellationToken cancellationToken)
    {
        if (entry.ItemKind == ShareItemKind.Secret)
        {
            var secret = await store.GetSecretAsync(entry.OwnerId, entry.ItemId, cancellationToken);
            return secret is null ? null : new ItemRow(secret.Value, null);
        }

        var folder = await store.GetFolderAsync(entry.OwnerId, entry.ItemId, cancellationToken);
        return folder is null ? null : new ItemRow(null, folder.Value);
    }

    private sealed record OwnerContext(VaultRead Read, Dictionary<(ShareItemKind Kind, Guid ItemId), ShareGrant> Grants, string DisplayName);

    private sealed record ItemRow(Secret? Secret, Folder? Folder);
}
