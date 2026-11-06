// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos;

public static class ApiRoutes
{
    public const string Me = "/api/me";

    public const string MeSessionCheck = "/api/me/session/check";

    public const string MeSessionRevoke = "/api/me/session/revoke";

    public const string AdminUsers = "/api/admin/users";

    public const string VaultSecrets = "/api/vault/secrets";

    public const string VaultSecret = "/api/vault/secrets/{secretId:guid}";

    public const string VaultSecretMove = "/api/vault/secrets/{secretId:guid}/move";

    public const string VaultSecretReveal = "/api/vault/secrets/{secretId:guid}/reveal";

    public const string VaultSecretHistory = "/api/vault/secrets/{secretId:guid}/history";

    public const string VaultSecretVersionReveal = "/api/vault/secrets/{secretId:guid}/history/{versionId:guid}/reveal";

    public const string VaultSecretVersionRestore = "/api/vault/secrets/{secretId:guid}/history/{versionId:guid}/restore";

    public const string VaultFolders = "/api/vault/folders";

    public const string VaultFolder = "/api/vault/folders/{folderId:guid}";

    public const string VaultFolderMove = "/api/vault/folders/{folderId:guid}/move";

    public const string RecycleBin = "/api/vault/recycle-bin";

    public const string RecycleBinSecretRestore = "/api/vault/recycle-bin/secrets/{secretId:guid}/restore";

    public const string RecycleBinFolderRestore = "/api/vault/recycle-bin/folders/{folderId:guid}/restore";

    public const string RecycleBinSecret = "/api/vault/recycle-bin/secrets/{secretId:guid}";

    public const string RecycleBinFolder = "/api/vault/recycle-bin/folders/{folderId:guid}";

    public const string SharedItems = "/api/shared/items";

    public const string SharedSecret = "/api/shared/owners/{ownerId:guid}/secrets/{secretId:guid}";

    public const string SharedSecretReveal = "/api/shared/owners/{ownerId:guid}/secrets/{secretId:guid}/reveal";

    public const string SharedSecretFiling = "/api/shared/owners/{ownerId:guid}/secrets/{secretId:guid}/filing";

    public const string SharedFolder = "/api/shared/owners/{ownerId:guid}/folders/{folderId:guid}";

    public const string SharedFolderFiling = "/api/shared/owners/{ownerId:guid}/folders/{folderId:guid}/filing";

    public const string SecretGrants = "/api/sharing/owners/{ownerId:guid}/secrets/{secretId:guid}/grants";

    public const string SecretGrant = "/api/sharing/owners/{ownerId:guid}/secrets/{secretId:guid}/grants/{recipientId:guid}";

    public const string FolderGrants = "/api/sharing/owners/{ownerId:guid}/folders/{folderId:guid}/grants";

    public const string FolderGrant = "/api/sharing/owners/{ownerId:guid}/folders/{folderId:guid}/grants/{recipientId:guid}";

    public const string DirectoryUsers = "/api/directory/users";

    public const string AdminUserLock = "/api/admin/users/{userId:guid}/lock";

    public const string AdminUserUnlock = "/api/admin/users/{userId:guid}/unlock";

    public const string AdminUserCounts = "/api/admin/users/{userId:guid}/counts";

    public const string AdminTransferPreview = "/api/admin/transfers/preview";

    public const string AdminTransfers = "/api/admin/transfers";

    public const string AdminTransfer = "/api/admin/transfers/{transferId:guid}";

    public const string AdminTransferApprove = "/api/admin/transfers/{transferId:guid}/approve";

    public const string AdminTransferReject = "/api/admin/transfers/{transferId:guid}/reject";

    public const string AdminTransferAbort = "/api/admin/transfers/{transferId:guid}/abort";

    public const string TransferNotices = "/api/transfers/notices";

    public const string TransferNotice = "/api/transfers/notices/{transferId:guid}";

    public const string TransferOwnedShares = "/api/transfers/shares";
}
