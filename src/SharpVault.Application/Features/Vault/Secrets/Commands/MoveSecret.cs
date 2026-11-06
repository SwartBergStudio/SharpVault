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

namespace SharpVault.Application.Features.Vault.Secrets.Commands;

public sealed record MoveSecretCommand(Guid SecretId, string ETag, Guid? FolderId) : IRequest<Result<SecretDto>>;

public sealed class MoveSecretCommandValidator : AbstractValidator<MoveSecretCommand>
{
    public MoveSecretCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
        RuleFor(x => x.ETag).ValidIfMatch();
        RuleFor(x => x.FolderId)
            .NotEqual(Guid.Empty)
            .When(x => x.FolderId is not null);
    }
}

public sealed class MoveSecretCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    ISecretAccessPolicy accessPolicy,
    VaultAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<MoveSecretCommandHandler> logger) : IRequestHandler<MoveSecretCommand, Result<SecretDto>>
{
    public async Task<Result<SecretDto>> Handle(MoveSecretCommand request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SecretId, SecretPermission.Edit, cancellationToken);
        if (grant is null)
        {
            return Result<SecretDto>.NotFound();
        }

        var secretTask = store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        var folderTask = request.FolderId is { } folderId
            ? store.GetFolderAsync(grant.OwnerId, folderId, cancellationToken)
            : Task.FromResult<Versioned<Folder>?>(null);
        var vaultTask = keyRings.OpenAsync(grant.OwnerId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(vaultTask, secretTask, folderTask);

        using var vault = await vaultTask;
        var current = await secretTask;
        var folder = await folderTask;
        if (current is null || vault is null || (request.FolderId is not null && folder is null))
        {
            return Result<SecretDto>.NotFound();
        }

        if (!string.Equals(current.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<SecretDto>.Conflict();
        }

        if (current.Value.FolderId == request.FolderId)
        {
            return Result<SecretDto>.Success(SecretDtoFactory.Create(current, vault));
        }

        var secret = current.Value;
        secret.FolderId = request.FolderId;
        secret.Revision++;
        secret.ModifiedAt = timeProvider.GetUtcNow();

        var changes = new List<VaultChange>();
        if (folder is not null)
        {
            changes.Add(VaultChange.TouchFolder(folder.Value.Id, folder.ETag));
        }

        changes.Add(VaultChange.ReplaceSecret(secret, current.ETag));

        var write = await store.SubmitAsync(grant.OwnerId, changes, cancellationToken);
        if (VaultWrites.ToFailure<SecretDto>(write, logger, secret.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretMoved, grant.OwnerId, secret.Id, secret.Id));

        return Result<SecretDto>.Success(SecretDtoFactory.Create(new Versioned<Secret>(secret, VaultWrites.ETagAt(write, changes.Count - 1)), vault));
    }
}
