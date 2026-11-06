// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public sealed partial class ShareGrantService(
    IVaultStore store,
    IShareInboxStore inbox,
    TimeProvider timeProvider,
    ILogger<ShareGrantService> logger)
{
    public async Task<VaultWriteResult> AddAsync(ShareGrant grant, Guid? filedFolderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var entry = new ShareInboxEntry
        {
            RecipientId = grant.RecipientId,
            OwnerId = grant.OwnerId,
            ItemKind = grant.ItemKind,
            ItemId = grant.ItemId,
            SharedAt = grant.GrantedAt,
            FiledFolderId = filedFolderId,
        };

        await inbox.AddIfAbsentAsync(entry, cancellationToken);
        var write = await store.SubmitAsync(grant.OwnerId, [VaultChange.AddShareGrant(grant)], cancellationToken);
        if (write.Result is StoreWriteResult.Written or StoreWriteResult.AlreadyExists)
        {
            await EnsureInboxRowAsync(entry);
        }

        return write;
    }

    public Task<ShareRevokeOutcome> RevokeAsync(
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        Guid recipientId,
        CancellationToken cancellationToken) =>
        RevokeAsync(ownerId, kind, itemId, recipientId, onRevoked: null, cancellationToken);

    public async Task<ShareRevokeOutcome> RevokeAsync(
        Guid ownerId,
        ShareItemKind kind,
        Guid itemId,
        Guid recipientId,
        Func<ShareGrant, Task>? onRevoked,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < SharingLimits.RevokeAttempts; attempt++)
        {
            var rows = await store.GetShareGrantRowsAsync(ownerId, kind, itemId, recipientId, cancellationToken);
            if (rows is null)
            {
                await RemoveInboxRowOfMissingGrantAsync(ownerId, kind, itemId, recipientId);
                return new ShareRevokeOutcome(ShareRevokeResult.NotFound, null);
            }

            var grant = rows.ByItem.Value;
            var change = VaultChange.DeleteShareGrant(grant, rows.ByItem.ETag, rows.ByRecipientETag);
            var write = await store.SubmitAsync(ownerId, [change], cancellationToken);
            write.Interruption?.Throw();
            if (write.Result == StoreWriteResult.Written)
            {
                if (onRevoked is not null)
                {
                    await onRevoked(grant);
                }

                await RemoveInboxRowAsync(ownerId, kind, itemId, recipientId);
                return new ShareRevokeOutcome(ShareRevokeResult.Revoked, grant);
            }
        }

        return new ShareRevokeOutcome(ShareRevokeResult.Conflict, null);
    }

    public Task<ShareRevokeAllOutcome> RevokeAllReceivedAsync(Guid recipientId, CancellationToken cancellationToken) =>
        RevokeAllReceivedAsync(recipientId, onRevoked: null, cancellationToken);

    public async Task<ShareRevokeAllOutcome> RevokeAllReceivedAsync(
        Guid recipientId,
        Func<ShareGrant, Task>? onRevoked,
        CancellationToken cancellationToken)
    {
        var revoked = new List<ShareGrant>();
        string? token = null;
        var emptyPages = 0;
        var complete = true;

        do
        {
            var page = await inbox.ListAsync(recipientId, null, SharingLimits.MaxPageSize, token, cancellationToken);
            emptyPages = page.Items.Count == 0 ? emptyPages + 1 : 0;

            foreach (var row in page.Items)
            {
                var entry = row.Value;
                var outcome = await RevokeAsync(entry.OwnerId, entry.ItemKind, entry.ItemId, recipientId, onRevoked, cancellationToken);
                if (outcome.Revoked is { } grant)
                {
                    revoked.Add(grant);
                }
                else if (outcome.Result == ShareRevokeResult.Conflict)
                {
                    complete = false;
                }
            }

            if (emptyPages >= SharingLimits.MaxEmptyInboxPagesPerCall && page.ContinuationToken is not null)
            {
                complete = false;
                break;
            }

            if (page.ContinuationToken is not null && page.ContinuationToken == token)
            {
                complete = false;
                break;
            }

            token = page.ContinuationToken;
        }
        while (token is not null);

        if (!complete)
        {
            LogRevokeAllIncomplete(logger, revoked.Count);
        }

        return new ShareRevokeAllOutcome(revoked, complete);
    }

    private async Task EnsureInboxRowAsync(ShareInboxEntry entry)
    {
        using var timeout = new CancellationTokenSource(SharingLimits.InboxCleanupTimeout, timeProvider);
        if (await inbox.GetAsync(entry.RecipientId, entry.OwnerId, entry.ItemKind, entry.ItemId, timeout.Token) is null)
        {
            await inbox.AddIfAbsentAsync(entry, timeout.Token);
        }
    }

    private async Task RemoveInboxRowOfMissingGrantAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId)
    {
        await RemoveInboxRowAsync(ownerId, kind, itemId, recipientId);

        using var timeout = new CancellationTokenSource(SharingLimits.InboxCleanupTimeout, timeProvider);
        try
        {
            var appeared = await store.GetShareGrantRowsAsync(ownerId, kind, itemId, recipientId, timeout.Token);
            if (appeared is not null)
            {
                var grant = appeared.ByItem.Value;
                var entry = new ShareInboxEntry
                {
                    RecipientId = recipientId,
                    OwnerId = ownerId,
                    ItemKind = kind,
                    ItemId = itemId,
                    SharedAt = grant.GrantedAt,
                };
                await inbox.AddIfAbsentAsync(entry, timeout.Token);
            }
        }
        catch (Exception exception)
        {
            LogInboxCleanupFailed(logger, exception.GetType().Name);
        }
    }

    private async Task RemoveInboxRowAsync(Guid ownerId, ShareItemKind kind, Guid itemId, Guid recipientId)
    {
        using var timeout = new CancellationTokenSource(SharingLimits.InboxCleanupTimeout, timeProvider);
        try
        {
            await inbox.RemoveAsync(recipientId, ownerId, kind, itemId, timeout.Token);
        }
        catch (Exception exception)
        {
            LogInboxCleanupFailed(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Share inbox cleanup failed ({ExceptionType})")]
    private static partial void LogInboxCleanupFailed(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Revoking all received shares did not finish after {RevokedCount} revoked")]
    private static partial void LogRevokeAllIncomplete(ILogger logger, int revokedCount);
}
