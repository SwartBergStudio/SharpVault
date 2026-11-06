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

public sealed record RestoreRecycledFolderCommand(Guid FolderId) : IRequest<Result<FolderDto>>;

public sealed class RestoreRecycledFolderCommandValidator : AbstractValidator<RestoreRecycledFolderCommand>
{
    public RestoreRecycledFolderCommandValidator()
    {
        RuleFor(x => x.FolderId).NotEqual(Guid.Empty);
    }
}

public sealed class RestoreRecycledFolderCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<RestoreRecycledFolderCommandHandler> logger) : IRequestHandler<RestoreRecycledFolderCommand, Result<FolderDto>>
{
    public async Task<Result<FolderDto>> Handle(RestoreRecycledFolderCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        var recycled = await store.GetRecycledFolderAsync(owner, request.FolderId, cancellationToken);
        if (recycled?.Value.DeletionBatchId is not { } batchId)
        {
            return Result<FolderDto>.NotFound();
        }

        using var vault = await keyRings.OpenAsync(owner, cancellationToken);
        if (vault is null)
        {
            return Result<FolderDto>.NotFound();
        }

        var activeTask = store.ListFoldersAsync(owner, cancellationToken);
        var batchTask = store.ListRecycledBatchAsync(owner, batchId, cancellationToken);
        await Task.WhenAll(activeTask, batchTask);

        var active = await activeTask;
        var scope = RecycledSubtree.Of(request.FolderId, await batchTask);

        if (scope is null)
        {
            return Result<FolderDto>.Rejected(VaultMessages.FolderTreeInvalid);
        }

        if (active.Count + scope.Folders.Count > VaultLimits.MaxFoldersPerVault)
        {
            return Result<FolderDto>.Rejected(VaultMessages.FolderLimitReached);
        }

        if (scope.Folders.Count + scope.Secrets.Count > VaultLimits.MaxItemsPerFolderOperation)
        {
            return Result<FolderDto>.Rejected(VaultMessages.FolderOperationTooLarge);
        }

        if (!TryResolveParent(scope, request.FolderId, active, out var parentId))
        {
            return Result<FolderDto>.Rejected(VaultMessages.FolderTreeInvalid);
        }

        var now = timeProvider.GetUtcNow();
        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(vault.KeyRingETag) };
        var items = new List<VaultAuditItem>();

        foreach (var entry in scope.Secrets)
        {
            var secret = entry.Value;
            secret.DeletedAt = null;
            secret.DeletionBatchId = null;
            secret.Revision++;
            secret.ModifiedAt = now;
            changes.Add(VaultChange.RestoreSecret(secret, entry.ETag));
            items.Add(new VaultAuditItem(AuditEventType.SecretRestored, owner, secret.Id, secret.Id));
        }

        foreach (var entry in scope.Folders.Reverse())
        {
            var folder = entry.Value;
            folder.DeletedAt = null;
            folder.DeletionBatchId = null;
            folder.ModifiedAt = now;
            if (folder.Id == request.FolderId)
            {
                folder.ParentId = parentId;
            }

            changes.Add(VaultChange.RestoreFolder(folder, entry.ETag));
            items.Add(new VaultAuditItem(AuditEventType.FolderRestored, owner, folder.Id, null));
        }

        var write = await store.SubmitAsync(owner, changes, cancellationToken);
        await audit.WriteCommittedManyAsync(VaultWrites.CommittedItems(write, items, leadingChanges: 1));
        if (VaultWrites.ToFailure<FolderDto>(write, logger, request.FolderId) is { } failure)
        {
            return failure;
        }

        var root = scope.Folders[0];
        return Result<FolderDto>.Success(FolderDtoFactory.Create(new Versioned<Folder>(root.Value, VaultWrites.ETagAt(write, changes.Count - 1)), vault));
    }

    private static bool TryResolveParent(RecycledSubtree scope, Guid rootId, IReadOnlyList<Versioned<Folder>> active, out Guid? parentId)
    {
        parentId = null;
        var originalParent = scope.Folders[0].Value.ParentId;
        if (originalParent is not { } parent)
        {
            return true;
        }

        try
        {
            var activeTree = new FolderTree(active.Select(folder => folder.Value));
            if (!activeTree.Contains(parent))
            {
                return true;
            }

            var height = new FolderTree(scope.Folders.Select(folder => folder.Value)).HeightOf(rootId);
            if (activeTree.DepthOf(parent) + height <= VaultLimits.MaxFolderDepth)
            {
                parentId = parent;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
