// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Secrets.Queries;

public sealed record ListSecretsQuery(Guid? FolderId, bool Unfiled, string? Search, int PageSize, string? ContinuationToken)
    : IRequest<Result<SecretPageDto>>
{
    public const int DefaultPageSize = VaultLimits.DefaultPageSize;

    public const int MaxPageSize = VaultLimits.MaxPageSize;
}

public sealed class ListSecretsQueryValidator : AbstractValidator<ListSecretsQuery>
{
    public ListSecretsQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListSecretsQuery.MaxPageSize);

        RuleFor(x => x.FolderId)
            .NotEqual(Guid.Empty)
            .When(x => x.FolderId is not null);

        RuleFor(x => x)
            .Must(query => !(query.FolderId is not null && query.Unfiled))
            .WithMessage(VaultMessages.FolderAndUnfiledConflict)
            .OverridePropertyName(nameof(ListSecretsQuery.Unfiled));

        RuleFor(x => x.Search)
            .SingleLineText(VaultLimits.SearchMaxLength)
            .When(x => x.Search is not null);

        RuleFor(x => x.ContinuationToken)
            .Must(ContinuationTokens.IsSecretToken)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(VaultMessages.InvalidContinuationToken);
    }
}

public sealed class ListSecretsQueryHandler(IVaultStore store, VaultKeyRingService keyRings, ICurrentUser currentUser)
    : IRequestHandler<ListSecretsQuery, Result<SecretPageDto>>
{
    public async Task<Result<SecretPageDto>> Handle(ListSecretsQuery request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        using var read = await keyRings.OpenForReadAsync(owner, cancellationToken);
        if (read.State == VaultReadState.NoRing)
        {
            return Result<SecretPageDto>.Success(new SecretPageDto());
        }

        var filter = FilterOf(request);
        try
        {
            if (request.Search is null)
            {
                return Result<SecretPageDto>.Success(await ListAsync(owner, filter, request, read.Vault, cancellationToken));
            }

            return Result<SecretPageDto>.Success(read.Vault is { } vault
                ? await SearchAsync(owner, filter, request, request.Search.Trim(), vault, cancellationToken)
                : new SecretPageDto());
        }
        catch (InvalidContinuationTokenException)
        {
            return Result<SecretPageDto>.Rejected(VaultMessages.InvalidContinuationToken);
        }
    }

    private static SecretFolderFilter FilterOf(ListSecretsQuery request) => request switch
    {
        { FolderId: { } folderId } => SecretFolderFilter.InFolder(folderId),
        { Unfiled: true } => SecretFolderFilter.Unfiled,
        _ => SecretFolderFilter.All,
    };

    private static bool Matches(SecretMetadata metadata, string term) =>
        Contains(metadata.Name, term)
        || Contains(metadata.Username, term)
        || Contains(metadata.Url, term)
        || metadata.Tags.Any(tag => Contains(tag, term));

    private static bool Contains(string? text, string term) =>
        text is not null && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private async Task<SecretPageDto> ListAsync(
        Guid owner,
        SecretFolderFilter filter,
        ListSecretsQuery request,
        OpenedVault? vault,
        CancellationToken cancellationToken)
    {
        var page = await store.ListSecretsAsync(owner, filter, request.PageSize, request.ContinuationToken, cancellationToken);
        return new SecretPageDto
        {
            Items = [.. page.Items.Select(item => SecretDtoFactory.Create(item, vault))],
            ContinuationToken = page.ContinuationToken,
        };
    }

    private async Task<SecretPageDto> SearchAsync(
        Guid owner,
        SecretFolderFilter filter,
        ListSecretsQuery request,
        string term,
        OpenedVault vault,
        CancellationToken cancellationToken)
    {
        var maxCalls = VaultLimits.SearchMaxScannedRows / VaultLimits.SearchScanBatchSize;
        var result = new SecretPageDto();
        var token = request.ContinuationToken;
        var scanned = 0;

        for (var calls = 1; ; calls++)
        {
            var page = await store.ListSecretsAsync(owner, filter, VaultLimits.SearchScanBatchSize, token, cancellationToken);
            for (var index = 0; index < page.Items.Count; index++)
            {
                var item = page.Items[index];
                scanned++;
                if (SecretMetadataCipher.TryDecrypt(vault.Session, item.Value, vault.MetadataKeyVersion, out var metadata) && Matches(metadata, term))
                {
                    result.Items.Add(SecretDtoFactory.Create(item, vault));
                }

                if (result.Items.Count >= request.PageSize)
                {
                    var isLastOfPage = index == page.Items.Count - 1;
                    result.ContinuationToken = isLastOfPage ? page.ContinuationToken : ContinuationTokens.ForSecret(item.Value.Id);
                    return result;
                }
            }

            token = page.ContinuationToken;
            if (token is null || scanned >= VaultLimits.SearchMaxScannedRows || calls >= maxCalls)
            {
                result.ContinuationToken = token;
                return result;
            }
        }
    }
}
