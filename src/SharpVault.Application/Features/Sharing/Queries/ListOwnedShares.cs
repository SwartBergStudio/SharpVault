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
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Sharing.Queries;

public sealed record ListOwnedSharesQuery(int PageSize, string? ContinuationToken) : IRequest<Result<OwnedSharePageDto>>;

public sealed class ListOwnedSharesQueryValidator : AbstractValidator<ListOwnedSharesQuery>
{
    public ListOwnedSharesQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, SharingLimits.MaxPageSize);
        RuleFor(x => x.ContinuationToken)
            .Must(ContinuationTokens.IsStorageToken)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(SharingMessages.InvalidContinuationToken);
    }
}

public sealed class ListOwnedSharesQueryHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    ICurrentUser currentUser) : IRequestHandler<ListOwnedSharesQuery, Result<OwnedSharePageDto>>
{
    public async Task<Result<OwnedSharePageDto>> Handle(ListOwnedSharesQuery request, CancellationToken cancellationToken)
    {
        var ownerId = currentUser.UserId;
        ShareGrantPage page;
        try
        {
            page = await store.ListShareGrantsByOwnerAsync(ownerId, request.PageSize, request.ContinuationToken, cancellationToken);
        }
        catch (InvalidContinuationTokenException)
        {
            return Result<OwnedSharePageDto>.Rejected(SharingMessages.InvalidContinuationToken);
        }

        var grants = page.Items
            .Select(item => item.Value)
            .Where(grant => grant.Permission != SharePermission.None)
            .ToList();

        var lookups = await LoadItemsAsync(ownerId, grants, cancellationToken);
        using var read = await keyRings.OpenForReadAsync(ownerId, cancellationToken);

        var items = grants
            .Select((grant, index) => ToDto(grant, NameOf(lookups[index], read)))
            .ToList();

        return Result<OwnedSharePageDto>.Success(new OwnedSharePageDto
        {
            Items = items,
            ContinuationToken = page.ContinuationToken,
        });
    }

    private async Task<IReadOnlyList<ItemLookup>> LoadItemsAsync(Guid ownerId, List<ShareGrant> grants, CancellationToken cancellationToken)
    {
        var results = new ItemLookup[grants.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = VaultLimits.FolderQueryParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, grants.Count), options, async (index, token) =>
        {
            results[index] = await LoadItemAsync(ownerId, grants[index], token);
        });

        return results;
    }

    private async Task<ItemLookup> LoadItemAsync(Guid ownerId, ShareGrant grant, CancellationToken cancellationToken)
    {
        if (grant.ItemKind == ShareItemKind.Secret)
        {
            var secret = await store.GetSecretAsync(ownerId, grant.ItemId, cancellationToken)
                ?? await store.GetRecycledSecretAsync(ownerId, grant.ItemId, cancellationToken);
            return new ItemLookup(secret, null);
        }

        var folder = await store.GetFolderAsync(ownerId, grant.ItemId, cancellationToken)
            ?? await store.GetRecycledFolderAsync(ownerId, grant.ItemId, cancellationToken);
        return new ItemLookup(null, folder);
    }

    private static string? NameOf(ItemLookup lookup, VaultRead read)
    {
        if (read.Vault is not { } vault)
        {
            return null;
        }

        if (lookup.Secret is { } secret)
        {
            return SecretMetadataCipher.TryDecrypt(vault.Session, secret.Value, vault.MetadataKeyVersion, out var metadata)
                ? metadata.Name
                : null;
        }

        if (lookup.Folder is { } folder)
        {
            return SecretMetadataCipher.TryDecryptFolderName(vault.Session, folder.Value, vault.MetadataKeyVersion, out var name)
                ? name
                : null;
        }

        return null;
    }

    private static OwnedShareDto ToDto(ShareGrant grant, string? name) => new()
    {
        Kind = SharingEnumMap.ToDto(grant.ItemKind),
        ItemId = grant.ItemId,
        Name = name,
        IsDamaged = name is null,
        RecipientId = grant.RecipientId,
        RecipientDisplayName = grant.RecipientDisplayName,
        Permission = SharingEnumMap.ToDto(grant.Permission),
        GrantedAt = grant.GrantedAt,
    };

    private sealed record ItemLookup(Versioned<Secret>? Secret, Versioned<Folder>? Folder);
}
