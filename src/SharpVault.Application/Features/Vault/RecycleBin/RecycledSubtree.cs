// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.RecycleBin;

public sealed record RecycledSubtree(IReadOnlyList<Versioned<Folder>> Folders, IReadOnlyList<Versioned<Secret>> Secrets)
{
    public static RecycledSubtree? Of(Guid rootFolderId, RecycledBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var byId = batch.Folders.ToDictionary(folder => folder.Value.Id);
        if (!byId.ContainsKey(rootFolderId))
        {
            return null;
        }

        IReadOnlyList<Guid> order;
        try
        {
            order = new FolderTree(batch.Folders.Select(folder => folder.Value)).SubtreeOf(rootFolderId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var members = order.ToHashSet();
        var secrets = batch.Secrets
            .Where(secret => secret.Value.FolderId is { } folderId && members.Contains(folderId))
            .OrderBy(secret => secret.Value.Id)
            .ToList();

        return new RecycledSubtree([.. order.Select(id => byId[id])], secrets);
    }
}
