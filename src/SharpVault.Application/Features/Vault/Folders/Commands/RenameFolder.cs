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

public sealed record RenameFolderCommand(Guid FolderId, string Name) : IRequest<Result<FolderDto>>
{
    public override string ToString() => nameof(RenameFolderCommand);
}

public sealed class RenameFolderCommandValidator : AbstractValidator<RenameFolderCommand>
{
    public RenameFolderCommandValidator()
    {
        RuleFor(x => x.FolderId).NotEqual(Guid.Empty);

        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(VaultMessages.FieldRequired)
            .SingleLineText(VaultLimits.FolderNameMaxLength);
    }
}

public sealed class RenameFolderCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<RenameFolderCommandHandler> logger) : IRequestHandler<RenameFolderCommand, Result<FolderDto>>
{
    public async Task<Result<FolderDto>> Handle(RenameFolderCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        var vaultTask = keyRings.OpenAsync(owner, cancellationToken);
        var folderTask = store.GetFolderAsync(owner, request.FolderId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(vaultTask, folderTask);

        using var vault = await vaultTask;
        var current = await folderTask;
        if (current is null || vault is null)
        {
            return Result<FolderDto>.NotFound();
        }

        if (FolderDtoFactory.Create(current, vault).IsDamaged)
        {
            return Result<FolderDto>.Rejected(VaultMessages.DamagedFolderNotRenamable);
        }

        var folder = current.Value;
        folder.MetadataKeyVersion = vault.MetadataKeyVersion;
        folder.ModifiedAt = timeProvider.GetUtcNow();
        SecretMetadataCipher.EncryptFolderName(vault.Session, folder, request.Name);

        var write = await store.SubmitAsync(owner, [VaultChange.ReplaceFolder(folder, current.ETag)], cancellationToken);
        if (VaultWrites.ToFailure<FolderDto>(write, logger, folder.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.FolderRenamed, owner, folder.Id, null));

        return Result<FolderDto>.Success(FolderDtoFactory.Create(new Versioned<Folder>(folder, VaultWrites.ETagAt(write, 0)), vault));
    }
}
