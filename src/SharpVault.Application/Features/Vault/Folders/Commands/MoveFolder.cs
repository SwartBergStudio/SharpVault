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

public sealed record MoveFolderCommand(Guid FolderId, Guid? ParentId) : IRequest<Result<FolderDto>>;

public sealed class MoveFolderCommandValidator : AbstractValidator<MoveFolderCommand>
{
    public MoveFolderCommandValidator()
    {
        RuleFor(x => x.FolderId).NotEqual(Guid.Empty);
        RuleFor(x => x.ParentId)
            .NotEqual(Guid.Empty)
            .When(x => x.ParentId is not null);
    }
}

public sealed class MoveFolderCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<MoveFolderCommandHandler> logger) : IRequestHandler<MoveFolderCommand, Result<FolderDto>>
{
    public async Task<Result<FolderDto>> Handle(MoveFolderCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        var keyRingETag = await keyRings.GetKeyRingETagAsync(owner, cancellationToken);
        if (keyRingETag is null)
        {
            return Result<FolderDto>.NotFound();
        }

        var folders = await store.ListFoldersAsync(owner, cancellationToken);
        var current = folders.FirstOrDefault(folder => folder.Value.Id == request.FolderId);
        if (current is null)
        {
            return Result<FolderDto>.NotFound();
        }

        var tree = new FolderTree(folders.Select(folder => folder.Value));
        if (current.Value.ParentId != request.ParentId && Check(tree, request) is { } rejection)
        {
            return rejection;
        }

        using var vault = await keyRings.OpenAsync(owner, cancellationToken);
        if (vault is null)
        {
            return Result<FolderDto>.NotFound();
        }

        if (current.Value.ParentId == request.ParentId)
        {
            return Result<FolderDto>.Success(FolderDtoFactory.Create(current, vault));
        }

        var folder = current.Value;
        folder.ParentId = request.ParentId;
        folder.ModifiedAt = timeProvider.GetUtcNow();

        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(keyRingETag), VaultChange.ReplaceFolder(folder, current.ETag) };
        var write = await store.SubmitAsync(owner, changes, cancellationToken);
        if (VaultWrites.ToFailure<FolderDto>(write, logger, folder.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.FolderMoved, owner, folder.Id, null));

        return Result<FolderDto>.Success(FolderDtoFactory.Create(new Versioned<Folder>(folder, VaultWrites.ETagAt(write, 1)), vault));
    }

    private static Result<FolderDto>? Check(FolderTree tree, MoveFolderCommand request)
    {
        try
        {
            if (request.ParentId is not { } parent)
            {
                return null;
            }

            if (!tree.Contains(parent))
            {
                return Result<FolderDto>.NotFound();
            }

            if (tree.IsSameOrDescendant(parent, request.FolderId))
            {
                return Result<FolderDto>.Rejected(VaultMessages.FolderCycle);
            }

            return tree.DepthOf(parent) + tree.HeightOf(request.FolderId) > VaultLimits.MaxFolderDepth
                ? Result<FolderDto>.Rejected(VaultMessages.FolderDepthExceeded)
                : null;
        }
        catch (InvalidOperationException)
        {
            return Result<FolderDto>.Rejected(VaultMessages.FolderTreeInvalid);
        }
    }
}
