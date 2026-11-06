// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Vault.RecycleBin;

public interface IRecycleBinPurger
{
    Task<RecycleBinPurgeResult> PurgeExpiredAsync(CancellationToken cancellationToken);
}
