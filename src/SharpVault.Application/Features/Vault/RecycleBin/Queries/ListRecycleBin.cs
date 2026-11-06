// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.RecycleBin.Queries;

public sealed record ListRecycleBinQuery(int PageSize, string? ContinuationToken) : IRequest<Result<RecycleBinPageDto>>
{
    public const int DefaultPageSize = VaultLimits.DefaultPageSize;

    public const int MaxPageSize = VaultLimits.MaxPageSize;
}

public sealed class ListRecycleBinQueryValidator : AbstractValidator<ListRecycleBinQuery>
{
    public ListRecycleBinQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListRecycleBinQuery.MaxPageSize);

        RuleFor(x => x.ContinuationToken)
            .Must(ContinuationTokens.IsRecycleBinToken)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(VaultMessages.InvalidContinuationToken);
    }
}

public sealed class ListRecycleBinQueryHandler(IVaultStore store, VaultKeyRingService keyRings, ICurrentUser currentUser)
    : IRequestHandler<ListRecycleBinQuery, Result<RecycleBinPageDto>>
{
    public async Task<Result<RecycleBinPageDto>> Handle(ListRecycleBinQuery request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        using var read = await keyRings.OpenForReadAsync(owner, cancellationToken);
        if (read.State == VaultReadState.NoRing)
        {
            return Result<RecycleBinPageDto>.Success(new RecycleBinPageDto());
        }

        try
        {
            var page = await store.ListRecycleBinAsync(owner, request.PageSize, request.ContinuationToken, cancellationToken);
            return Result<RecycleBinPageDto>.Success(new RecycleBinPageDto
            {
                Items = [.. page.Items.Select(entry => ToDto(entry, read.Vault))],
                ContinuationToken = page.ContinuationToken,
            });
        }
        catch (InvalidContinuationTokenException)
        {
            return Result<RecycleBinPageDto>.Rejected(VaultMessages.InvalidContinuationToken);
        }
    }

    private static RecycleBinItemDto ToDto(RecycleBinEntry entry, OpenedVault? vault)
    {
        if (entry.Folder is { } folder)
        {
            var dto = FolderDtoFactory.Create(folder, vault);
            return Build(RecycleBinItemKindDto.Folder, dto.Id, dto.Name, null, dto.IsDamaged, folder.Value.DeletedAt, folder.Value.DeletionBatchId);
        }

        var secret = entry.Secret ?? throw new InvalidOperationException("A recycle bin entry holds neither a secret nor a folder.");
        var secretDto = SecretDtoFactory.Create(secret, vault);
        return Build(RecycleBinItemKindDto.Secret, secretDto.Id, secretDto.Name, secretDto.Type, secretDto.IsDamaged, secret.Value.DeletedAt, secret.Value.DeletionBatchId);
    }

    private static RecycleBinItemDto Build(
        RecycleBinItemKindDto kind,
        Guid id,
        string? name,
        SecretTypeDto? type,
        bool isDamaged,
        DateTimeOffset? deletedAt,
        Guid? batchId)
    {
        var deleted = deletedAt.GetValueOrDefault();
        return new RecycleBinItemDto
        {
            Kind = kind,
            Id = id,
            Name = name,
            Type = type,
            DeletionBatchId = batchId.GetValueOrDefault(),
            DeletedAt = deleted,
            PurgeAfter = deleted + VaultLimits.RecycleBinRetention,
            IsDamaged = isDamaged,
        };
    }
}
