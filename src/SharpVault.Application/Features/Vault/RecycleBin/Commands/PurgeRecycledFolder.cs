// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.RecycleBin.Commands;

public sealed record PurgeRecycledFolderCommand(Guid FolderId) : IRequest<Result<Done>>;

public sealed class PurgeRecycledFolderCommandValidator : AbstractValidator<PurgeRecycledFolderCommand>
{
    public PurgeRecycledFolderCommandValidator()
    {
        RuleFor(x => x.FolderId).NotEqual(Guid.Empty);
    }
}

public sealed partial class PurgeRecycledFolderCommandHandler(
    IVaultStore store,
    RecycledItemEraser eraser,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    ILogger<PurgeRecycledFolderCommandHandler> logger) : IRequestHandler<PurgeRecycledFolderCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(PurgeRecycledFolderCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        var root = await store.GetRecycledFolderAsync(owner, request.FolderId, cancellationToken);
        if (root?.Value.DeletionBatchId is not { } batchId)
        {
            return Result<Done>.NotFound();
        }

        var batch = await store.ListRecycledBatchAsync(owner, batchId, cancellationToken);
        var scope = RecycledSubtree.Of(request.FolderId, batch);
        if (scope is null)
        {
            return Result<Done>.Rejected(VaultMessages.FolderTreeInvalid);
        }

        if (scope.Folders.Count + scope.Secrets.Count > VaultLimits.MaxItemsPerFolderOperation)
        {
            return Result<Done>.Rejected(VaultMessages.FolderOperationTooLarge);
        }

        var outcomes = new ConcurrentBag<(Versioned<Secret> Secret, ErasedItems Erased)>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = VaultLimits.FolderQueryParallelism,
            CancellationToken = cancellationToken,
        };

        var failures = new ConcurrentQueue<Exception>();
        try
        {
            await Parallel.ForEachAsync(scope.Secrets, options, async (secret, token) =>
            {
                try
                {
                    outcomes.Add((secret, await eraser.EraseSecretAsync(owner, secret, token)));
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            });
        }
        catch (OperationCanceledException exception)
        {
            failures.Enqueue(exception);
        }

        var grantItems = outcomes
            .SelectMany(entry => entry.Erased.CommittedGrantAuditItems())
            .OrderBy(item => item.TargetId)
            .ThenBy(item => item.RecipientId)
            .ToList();
        var erased = outcomes
            .Where(entry => entry.Erased.Write.Result == StoreWriteResult.Written)
            .Select(entry => new VaultAuditItem(AuditEventType.SecretPurged, owner, entry.Secret.Value.Id, entry.Secret.Value.Id))
            .OrderBy(item => item.TargetId)
            .ToList();
        await audit.WriteCommittedManyAsync([.. grantItems, .. erased]);

        if (failures.TryPeek(out _))
        {
            var first = failures.FirstOrDefault(exception => exception is not OperationCanceledException) ?? failures.First();
            LogEraseFailures(logger, failures.Count, erased.Count, first.GetType().Name);
            ExceptionDispatchInfo.Capture(first).Throw();
        }

        var failedWrite = outcomes.Select(entry => entry.Erased.Write)
            .Where(write => write.Interruption is not null || write.Result != StoreWriteResult.Written)
            .OrderBy(write => write.Interruption is null)
            .FirstOrDefault();
        if (failedWrite is not null && VaultWrites.ToFailure<Done>(failedWrite, logger, request.FolderId) is { } secretFailure)
        {
            return secretFailure;
        }

        var erasureOrder = scope.Folders.Reverse().ToList();
        var folderItems = erasureOrder
            .Select(folder => new VaultAuditItem(AuditEventType.FolderPurged, owner, folder.Value.Id, null))
            .ToList();
        var folderErased = await eraser.EraseFoldersAsync(owner, erasureOrder, cancellationToken);
        await audit.WriteCommittedManyAsync([.. folderErased.CommittedGrantAuditItems(), .. folderErased.CommittedAfterGrants(folderItems)]);
        if (VaultWrites.ToFailure<Done>(folderErased.Write, logger, request.FolderId) is { } failure)
        {
            return failure;
        }

        return Result<Done>.Success(Done.Value);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Purging a recycled folder failed for {FailedCount} secrets after {ErasedCount} were erased and audited: {ExceptionType}")]
    private static partial void LogEraseFailures(ILogger logger, int failedCount, int erasedCount, string exceptionType);
}
