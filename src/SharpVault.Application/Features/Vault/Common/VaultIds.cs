// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using System.Text;

namespace SharpVault.Application.Features.Vault.Common;

public static class VaultIds
{
    private const string FolderDeletionBatchSeed = "sharpvault.folder-deletion-batch.";
    private const string TransferRootFolderSeed = "sharpvault.transfer-root-folder.";
    private const string TransferredSecretSeed = "sharpvault.transferred-secret.";
    private const string GuidFormat = "N";
    private const int GuidByteLength = 16;

    public static Guid NewId(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        return Guid.CreateVersion7(time.GetUtcNow());
    }

    public static Guid DeletionBatchIdFor(Guid folderId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FolderDeletionBatchSeed + folderId.ToString(GuidFormat)));
        return new Guid(hash.AsSpan(0, GuidByteLength));
    }

    public static Guid TransferRootFolderIdFor(Guid transferId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(TransferRootFolderSeed + transferId.ToString(GuidFormat)));
        return new Guid(hash.AsSpan(0, GuidByteLength));
    }

    public static Guid TransferredSecretIdFor(Guid transferId, Guid secretId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(TransferredSecretSeed + transferId.ToString(GuidFormat) + "." + secretId.ToString(GuidFormat)));
        return new Guid(hash.AsSpan(0, GuidByteLength));
    }
}
