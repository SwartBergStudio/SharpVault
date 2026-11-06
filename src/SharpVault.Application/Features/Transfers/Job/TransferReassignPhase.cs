// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferReassignPhase(
    IVaultStore vaults,
    ShareGrantService shares,
    IShareInboxStore inbox,
    AdminAuditWriter audit)
{
    public async Task<bool> RunAsync(TransferJob job, TransferLeaseKeeper lease, CancellationToken cancellationToken)
    {
        var blocked = 0;
        string? token = null;
        do
        {
            await lease.RenewIfDueAsync(cancellationToken);
            var page = await vaults.ListShareGrantsByOwnerAsync(job.SourceUserId, TransferLimits.GrantPageSize, token, cancellationToken);
            foreach (var grant in page.Items.Select(item => item.Value))
            {
                await lease.RenewIfDueAsync(cancellationToken);
                var targetItemId = await TargetItemIdAsync(job, grant, cancellationToken);
                if (targetItemId is { } resolvedId && !await MoveGrantAsync(job, grant, resolvedId, cancellationToken))
                {
                    blocked++;
                }
            }

            token = page.ContinuationToken;
        }
        while (token is not null);

        return blocked == 0;
    }

    private async Task<Guid?> TargetItemIdAsync(TransferJob job, ShareGrant grant, CancellationToken cancellationToken)
    {
        if (grant.ItemKind == ShareItemKind.Secret)
        {
            return await SecretTargetIdAsync(job, grant.ItemId, cancellationToken);
        }

        return await FolderExistsAsync(job.TargetUserId, grant.ItemId, cancellationToken) ? grant.ItemId : null;
    }

    private async Task<Guid?> SecretTargetIdAsync(TransferJob job, Guid secretId, CancellationToken cancellationToken)
    {
        if (await SecretExistsAsync(job.SourceUserId, secretId, cancellationToken))
        {
            return null;
        }

        var renamed = VaultIds.TransferredSecretIdFor(job.Id, secretId);
        if (await SecretExistsAsync(job.TargetUserId, renamed, cancellationToken))
        {
            return renamed;
        }

        return await SecretExistsAsync(job.TargetUserId, secretId, cancellationToken) ? secretId : null;
    }

    private async Task<bool> SecretExistsAsync(Guid ownerId, Guid secretId, CancellationToken cancellationToken) =>
        await vaults.GetSecretAsync(ownerId, secretId, cancellationToken) is not null
        || await vaults.GetRecycledSecretAsync(ownerId, secretId, cancellationToken) is not null;

    private async Task<bool> FolderExistsAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        await vaults.GetFolderAsync(ownerId, folderId, cancellationToken) is not null
        || await vaults.GetRecycledFolderAsync(ownerId, folderId, cancellationToken) is not null;

    private async Task<bool> MoveGrantAsync(TransferJob job, ShareGrant grant, Guid targetItemId, CancellationToken cancellationToken)
    {
        if (grant.RecipientId != job.TargetUserId)
        {
            var filing = await inbox.GetAsync(grant.RecipientId, job.SourceUserId, grant.ItemKind, grant.ItemId, cancellationToken);
            var copy = new ShareGrant
            {
                OwnerId = job.TargetUserId,
                ItemKind = grant.ItemKind,
                ItemId = targetItemId,
                RecipientId = grant.RecipientId,
                Permission = grant.Permission,
                RecipientDisplayName = grant.RecipientDisplayName,
                RecipientEmail = grant.RecipientEmail,
                GrantedById = grant.GrantedById,
                GrantedByDisplayName = grant.GrantedByDisplayName,
                GrantedAt = grant.GrantedAt,
                ModifiedAt = grant.ModifiedAt,
            };

            await audit.WriteAsync(TransferAuditItems.Reassigned(job, grant, targetItemId), cancellationToken);
            var added = await shares.AddAsync(copy, filing?.Value.FiledFolderId, cancellationToken);
            added.Interruption?.Throw();
            if (added.Result is not (StoreWriteResult.Written or StoreWriteResult.AlreadyExists))
            {
                return false;
            }
        }

        await audit.WriteAsync(TransferAuditItems.RevokedByTransfer(job, grant), cancellationToken);
        var outcome = await shares.RevokeAsync(job.SourceUserId, grant.ItemKind, grant.ItemId, grant.RecipientId, cancellationToken);
        return outcome.Result is ShareRevokeResult.Revoked or ShareRevokeResult.NotFound;
    }
}
