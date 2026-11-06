// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Features.Vault.Common;

public static partial class VaultWrites
{
    public static Result<T>? ToFailure<T>(VaultWriteResult write, ILogger logger, Guid itemId)
    {
        ArgumentNullException.ThrowIfNull(write);

        write.Interruption?.Throw();
        return ToFailure<T>(write.Result, logger, itemId);
    }

    public static Result<T>? ToFailure<T>(StoreWriteResult result, ILogger logger, Guid itemId)
    {
        ArgumentNullException.ThrowIfNull(logger);

        switch (result)
        {
            case StoreWriteResult.Written:
                return null;
            case StoreWriteResult.NotFound:
                return Result<T>.NotFound();
            case StoreWriteResult.Conflict:
            case StoreWriteResult.AlreadyExists:
                LogWriteConflict(logger, itemId);
                return Result<T>.Conflict();
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }

    public static IReadOnlyList<VaultAuditItem> CommittedItems(VaultWriteResult write, IReadOnlyList<VaultAuditItem> items, int leadingChanges)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(items);

        if (write.Result == StoreWriteResult.Written)
        {
            return items;
        }

        var committed = Math.Clamp(write.CompletedChanges - leadingChanges, 0, items.Count);
        return committed == 0 ? [] : [.. items.Take(committed)];
    }

    public static string ETagAt(VaultWriteResult write, int index)
    {
        ArgumentNullException.ThrowIfNull(write);

        return write.ETags[index] ?? throw new InvalidOperationException("The store did not return an ETag for the written item.");
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vault write conflict on {ItemId}")]
    private static partial void LogWriteConflict(ILogger logger, Guid itemId);
}
