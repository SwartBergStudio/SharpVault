// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Folders.Commands;

public sealed record CreateFolderCommand(string Name, Guid? ParentId) : IRequest<Result<FolderDto>>
{
    public override string ToString() => nameof(CreateFolderCommand);
}

public sealed class CreateFolderCommandValidator : AbstractValidator<CreateFolderCommand>
{
    public CreateFolderCommandValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(VaultMessages.FieldRequired)
            .SingleLineText(VaultLimits.FolderNameMaxLength);

        RuleFor(x => x.ParentId)
            .NotEqual(Guid.Empty)
            .When(x => x.ParentId is not null);
    }
}

public sealed class CreateFolderCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<CreateFolderCommandHandler> logger) : IRequestHandler<CreateFolderCommand, Result<FolderDto>>
{
    public async Task<Result<FolderDto>> Handle(CreateFolderCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        using var vault = await keyRings.OpenOrCreateAsync(owner, cancellationToken);
        var folders = await store.ListFoldersAsync(owner, cancellationToken);

        if (Validate(request.ParentId, folders) is { } rejection)
        {
            return rejection;
        }

        var now = timeProvider.GetUtcNow();
        var folder = new Folder
        {
            Id = VaultIds.NewId(timeProvider),
            OwnerId = owner,
            ParentId = request.ParentId,
            MetadataKeyVersion = vault.MetadataKeyVersion,
            CreatedAt = now,
            ModifiedAt = now,
        };

        SecretMetadataCipher.EncryptFolderName(vault.Session, folder, request.Name);

        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(vault.KeyRingETag), VaultChange.AddFolder(folder) };
        var write = await store.SubmitAsync(owner, changes, cancellationToken);
        if (VaultWrites.ToFailure<FolderDto>(write, logger, folder.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.FolderCreated, owner, folder.Id, null));

        var etag = VaultWrites.ETagAt(write, changes.Count - 1);
        return Result<FolderDto>.Success(FolderDtoFactory.Create(new Versioned<Folder>(folder, etag), vault));
    }

    private static Result<FolderDto>? Validate(Guid? parentId, IReadOnlyList<Versioned<Folder>> folders)
    {
        var tree = new FolderTree(folders.Select(folder => folder.Value));
        if (tree.Count >= VaultLimits.MaxFoldersPerVault)
        {
            return Result<FolderDto>.Rejected(VaultMessages.FolderLimitReached);
        }

        if (parentId is not { } parent)
        {
            return null;
        }

        if (!tree.Contains(parent))
        {
            return Result<FolderDto>.NotFound();
        }

        try
        {
            return tree.DepthOf(parent) + 1 > VaultLimits.MaxFolderDepth
                ? Result<FolderDto>.Rejected(VaultMessages.FolderDepthExceeded)
                : null;
        }
        catch (InvalidOperationException)
        {
            return Result<FolderDto>.Rejected(VaultMessages.FolderTreeInvalid);
        }
    }
}
