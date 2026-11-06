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

namespace SharpVault.Application.Features.Vault.RecycleBin.Commands;

public sealed record RestoreRecycledSecretCommand(Guid SecretId) : IRequest<Result<SecretDto>>;

public sealed class RestoreRecycledSecretCommandValidator : AbstractValidator<RestoreRecycledSecretCommand>
{
    public RestoreRecycledSecretCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
    }
}

public sealed class RestoreRecycledSecretCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<RestoreRecycledSecretCommandHandler> logger) : IRequestHandler<RestoreRecycledSecretCommand, Result<SecretDto>>
{
    public async Task<Result<SecretDto>> Handle(RestoreRecycledSecretCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        var binTask = store.GetRecycledSecretAsync(owner, request.SecretId, cancellationToken);
        var vaultTask = keyRings.OpenAsync(owner, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(vaultTask, binTask);

        using var vault = await vaultTask;
        var recycled = await binTask;
        if (recycled is null || vault is null)
        {
            return Result<SecretDto>.NotFound();
        }

        var secret = recycled.Value;
        var folder = secret.FolderId is { } folderId
            ? await store.GetFolderAsync(owner, folderId, cancellationToken)
            : null;

        secret.FolderId = folder?.Value.Id;
        secret.DeletedAt = null;
        secret.DeletionBatchId = null;
        secret.Revision++;
        secret.ModifiedAt = timeProvider.GetUtcNow();

        var changes = new List<VaultChange>();
        if (folder is not null)
        {
            changes.Add(VaultChange.TouchFolder(folder.Value.Id, folder.ETag));
        }

        changes.Add(VaultChange.RestoreSecret(secret, recycled.ETag));

        var write = await store.SubmitAsync(owner, changes, cancellationToken);
        if (VaultWrites.ToFailure<SecretDto>(write, logger, secret.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretRestored, owner, secret.Id, secret.Id));

        return Result<SecretDto>.Success(SecretDtoFactory.Create(new Versioned<Secret>(secret, VaultWrites.ETagAt(write, changes.Count - 1)), vault));
    }
}
