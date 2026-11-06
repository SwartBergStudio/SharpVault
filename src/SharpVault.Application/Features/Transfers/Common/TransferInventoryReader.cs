// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferInventoryReader
{
    public static async Task<TransferInventory> ReadAsync(IVaultStore vaults, Guid ownerId, int maxPages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vaults);

        var rows = new List<VaultInventoryRow>();
        string? token = null;
        var pages = 0;
        do
        {
            var page = await vaults.ListInventoryAsync(ownerId, token, cancellationToken);
            rows.AddRange(page.Rows);
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < maxPages);

        return new TransferInventory(rows, token is not null);
    }
}
