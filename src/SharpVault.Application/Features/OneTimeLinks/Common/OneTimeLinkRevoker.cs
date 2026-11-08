// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed class OneTimeLinkRevoker(IOneTimeLinkStore store, OneTimeLinkAuditWriter auditWriter)
{
    private const int RevokePageSize = 50;

    public async Task<bool> RevokeAsync(OneTimeLinkIndexEntry entry, Guid actorId, string? ipAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var current = await store.GetAsync(entry.LinkId, cancellationToken);
        var won = current is not null && await store.TryConsumeAsync(entry.LinkId, current.ETag, cancellationToken);

        try
        {
            if (won)
            {
                await auditWriter.WriteCommittedAsync(new OneTimeLinkAuditItem(
                    AuditEventType.OneTimeLinkRevoked,
                    actorId,
                    entry.LinkId,
                    entry.OwnerId,
                    entry.SecretId,
                    ipAddress,
                    UserAgent: null));
            }
        }
        finally
        {
            await store.RemoveBookkeepingAsync(entry.CreatorId, entry.LinkId, entry.ExpiresAt, cancellationToken);
        }

        return won;
    }

    public async Task RevokeAllForCreatorAsync(Guid creatorId, Guid actorId, string? ipAddress, CancellationToken cancellationToken)
    {
        while (true)
        {
            var page = await store.ListIndexAsync(creatorId, activeAt: null, RevokePageSize, afterLinkId: null, cancellationToken);
            if (page.Items.Count == 0)
            {
                return;
            }

            foreach (var entry in page.Items)
            {
                await RevokeAsync(entry, actorId, ipAddress, cancellationToken);
            }
        }
    }
}
