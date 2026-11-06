// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferRootFolder
{
    public const string RootSourceETag = "root";

    public static Guid IdOf(TransferJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.RootFolderId ?? VaultIds.TransferRootFolderIdFor(job.Id);
    }
}
