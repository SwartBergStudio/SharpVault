// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
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

public sealed record DeleteFolderCommand(Guid FolderId, FolderDeleteModeDto Mode) : IRequest<Result<Done>>;

public sealed class DeleteFolderCommandValidator : AbstractValidator<DeleteFolderCommand>
{
    public DeleteFolderCommandValidator()
    {
        RuleFor(x => x.FolderId).NotEqual(Guid.Empty);
        RuleFor(x => x.Mode).IsInEnum();
    }
}

public sealed class DeleteFolderCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<DeleteFolderCommandHandler> logger) : IRequestHandler<DeleteFolderCommand, Result<Done>>
{
    private const int FolderOperationLimit = VaultLimits.MaxItemsPerFolderOperation;

    public async Task<Result<Done>> Handle(DeleteFolderCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        var keyRingETag = await keyRings.GetKeyRingETagAsync(owner, cancellationToken);
        if (keyRingETag is null)
        {
            return Result<Done>.NotFound();
        }

        var folders = (await store.ListFoldersAsync(owner, cancellationToken)).ToDictionary(folder => folder.Value.Id);
        if (!folders.ContainsKey(request.FolderId))
        {
            return Result<Done>.NotFound();
        }

        var tree = new FolderTree(folders.Values.Select(folder => folder.Value));
        var plan = request.Mode == FolderDeleteModeDto.MoveToParent
            ? await PlanMoveToParentAsync(owner, request.FolderId, folders, tree, cancellationToken)
            : await PlanDeleteContentsAsync(owner, request.FolderId, folders, tree, cancellationToken);

        if (plan.Rejection is not null)
        {
            return Result<Done>.Rejected(plan.Rejection);
        }

        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(keyRingETag) };
        changes.AddRange(plan.Changes);

        var write = await store.SubmitAsync(owner, changes, cancellationToken);
        await audit.WriteCommittedManyAsync(VaultWrites.CommittedItems(write, plan.Audit, leadingChanges: 1));
        if (VaultWrites.ToFailure<Done>(write, logger, request.FolderId) is { } failure)
        {
            return failure;
        }

        return Result<Done>.Success(Done.Value);
    }

    private async Task<DeletionPlan> PlanMoveToParentAsync(
        Guid owner,
        Guid folderId,
        Dictionary<Guid, Versioned<Folder>> folders,
        FolderTree tree,
        CancellationToken cancellationToken)
    {
        var target = folders[folderId];
        var parentId = target.Value.ParentId is { } parent && folders.ContainsKey(parent) ? parent : (Guid?)null;
        var secrets = await store.ListSecretsInFolderAsync(owner, folderId, FolderOperationLimit + 1, cancellationToken);
        var children = tree.ChildrenOf(folderId);
        if (secrets.Count + children.Count + 1 > FolderOperationLimit)
        {
            return DeletionPlan.Reject(VaultMessages.FolderOperationTooLarge);
        }

        var now = timeProvider.GetUtcNow();
        var changes = new List<VaultChange>();
        var auditItems = new List<VaultAuditItem>();

        foreach (var entry in secrets)
        {
            var secret = entry.Value;
            secret.FolderId = parentId;
            secret.Revision++;
            secret.ModifiedAt = now;
            changes.Add(VaultChange.ReplaceSecret(secret, entry.ETag));
            auditItems.Add(new VaultAuditItem(AuditEventType.SecretMoved, owner, secret.Id, secret.Id));
        }

        foreach (var childId in children)
        {
            var child = folders[childId];
            child.Value.ParentId = parentId;
            child.Value.ModifiedAt = now;
            changes.Add(VaultChange.ReplaceFolder(child.Value, child.ETag));
            auditItems.Add(new VaultAuditItem(AuditEventType.FolderMoved, owner, childId, null));
        }

        var batchId = Guid.NewGuid();
        target.Value.DeletedAt = now;
        target.Value.DeletionBatchId = batchId;
        changes.Add(VaultChange.RecycleFolder(target.Value, target.ETag));
        auditItems.Add(new VaultAuditItem(AuditEventType.FolderDeleted, owner, folderId, null));

        return new DeletionPlan(null, changes, auditItems);
    }

    private async Task<DeletionPlan> PlanDeleteContentsAsync(
        Guid owner,
        Guid folderId,
        Dictionary<Guid, Versioned<Folder>> folders,
        FolderTree tree,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> subtree;
        try
        {
            subtree = tree.SubtreeOf(folderId);
        }
        catch (InvalidOperationException)
        {
            return DeletionPlan.Reject(VaultMessages.FolderTreeInvalid);
        }

        var secrets = await ListSecretsOfAsync(owner, subtree, cancellationToken);
        if (secrets is null || secrets.Count + subtree.Count > FolderOperationLimit)
        {
            return DeletionPlan.Reject(VaultMessages.FolderOperationTooLarge);
        }

        var now = timeProvider.GetUtcNow();
        var batchId = VaultIds.DeletionBatchIdFor(folderId);
        var changes = new List<VaultChange>();
        var auditItems = new List<VaultAuditItem>();

        foreach (var entry in secrets)
        {
            var secret = entry.Value;
            secret.DeletedAt = now;
            secret.DeletionBatchId = batchId;
            changes.Add(VaultChange.RecycleSecret(secret, entry.ETag));
            auditItems.Add(new VaultAuditItem(AuditEventType.SecretDeleted, owner, secret.Id, secret.Id));
        }

        foreach (var id in subtree.Reverse())
        {
            var folder = folders[id];
            folder.Value.DeletedAt = now;
            folder.Value.DeletionBatchId = batchId;
            changes.Add(VaultChange.RecycleFolder(folder.Value, folder.ETag));
            auditItems.Add(new VaultAuditItem(AuditEventType.FolderDeleted, owner, id, null));
        }

        return new DeletionPlan(null, changes, auditItems);
    }

    private async Task<List<Versioned<Secret>>?> ListSecretsOfAsync(Guid owner, IReadOnlyList<Guid> folderIds, CancellationToken cancellationToken)
    {
        var found = new ConcurrentBag<Versioned<Secret>>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = VaultLimits.FolderQueryParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(folderIds, options, async (id, token) =>
        {
            foreach (var secret in await store.ListSecretsInFolderAsync(owner, id, FolderOperationLimit + 1, token))
            {
                found.Add(secret);
            }
        });

        return found.Count > FolderOperationLimit ? null : [.. found.OrderBy(secret => secret.Value.Id)];
    }

    private sealed record DeletionPlan(string? Rejection, List<VaultChange> Changes, List<VaultAuditItem> Audit)
    {
        public static DeletionPlan Reject(string message) => new(message, [], []);
    }
}
