// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Features.Vault.RecycleBin;

public sealed record ErasedItems(VaultWriteResult Write, IReadOnlyList<ShareGrant> Grants)
{
    public IReadOnlyList<ShareGrant> CommittedGrants => CommittedGrantsOf();

    public IReadOnlyList<VaultAuditItem> CommittedGrantAuditItems() =>
        [.. CommittedGrants.Select(ShareAuditItems.Erased)];

    public IReadOnlyList<VaultAuditItem> CommittedAfterGrants(IReadOnlyList<VaultAuditItem> items) =>
        VaultWrites.CommittedItems(Write, items, Grants.Count);

    private IReadOnlyList<ShareGrant> CommittedGrantsOf()
    {
        if (Write.Result == StoreWriteResult.Written)
        {
            return Grants;
        }

        var committed = Math.Clamp(Write.CompletedChanges, 0, Grants.Count);
        return committed == 0 ? [] : [.. Grants.Take(committed)];
    }
}
