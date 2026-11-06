// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Common.Models;

public sealed record VaultChange
{
    private VaultChange(
        VaultChangeKind kind,
        Guid itemId,
        string? etag,
        Secret? secret = null,
        Folder? folder = null,
        SecretVersion? version = null,
        ShareGrant? grant = null,
        string? secondaryETag = null)
    {
        Kind = kind;
        ItemId = itemId;
        ETag = etag;
        Secret = secret;
        Folder = folder;
        Version = version;
        Grant = grant;
        SecondaryETag = secondaryETag;
    }

    public VaultChangeKind Kind { get; }

    public Secret? Secret { get; }

    public Folder? Folder { get; }

    public SecretVersion? Version { get; }

    public ShareGrant? Grant { get; }

    public Guid ItemId { get; }

    public string? ETag { get; }

    public string? SecondaryETag { get; }

    public static VaultChange AddSecret(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return new(VaultChangeKind.AddSecret, secret.Id, null, secret: secret);
    }

    public static VaultChange ReplaceSecret(Secret secret, string etag) => ForSecret(VaultChangeKind.ReplaceSecret, secret, etag);

    public static VaultChange RecycleSecret(Secret secret, string activeETag)
    {
        ArgumentNullException.ThrowIfNull(secret);
        RequireDeleted(secret.DeletedAt, secret.DeletionBatchId);
        return ForSecret(VaultChangeKind.RecycleSecret, secret, activeETag);
    }

    public static VaultChange RestoreSecret(Secret secret, string recycledETag)
    {
        ArgumentNullException.ThrowIfNull(secret);
        RequireNotDeleted(secret.DeletedAt, secret.DeletionBatchId);
        return ForSecret(VaultChangeKind.RestoreSecret, secret, recycledETag);
    }

    public static VaultChange PurgeSecret(Secret secret, string recycledETag) => ForSecret(VaultChangeKind.PurgeSecret, secret, recycledETag);

    public static VaultChange AddVersion(SecretVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new(VaultChangeKind.AddVersion, version.Id, null, version: version);
    }

    public static VaultChange DeleteVersion(SecretVersion version, string etag)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        return new(VaultChangeKind.DeleteVersion, version.Id, etag, version: version);
    }

    public static VaultChange AddFolder(Folder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new(VaultChangeKind.AddFolder, folder.Id, null, folder: folder);
    }

    public static VaultChange ReplaceFolder(Folder folder, string etag) => ForFolder(VaultChangeKind.ReplaceFolder, folder, etag);

    public static VaultChange RecycleFolder(Folder folder, string activeETag)
    {
        ArgumentNullException.ThrowIfNull(folder);
        RequireDeleted(folder.DeletedAt, folder.DeletionBatchId);
        return ForFolder(VaultChangeKind.RecycleFolder, folder, activeETag);
    }

    public static VaultChange RestoreFolder(Folder folder, string recycledETag)
    {
        ArgumentNullException.ThrowIfNull(folder);
        RequireNotDeleted(folder.DeletedAt, folder.DeletionBatchId);
        return ForFolder(VaultChangeKind.RestoreFolder, folder, recycledETag);
    }

    public static VaultChange PurgeFolder(Folder folder, string recycledETag) => ForFolder(VaultChangeKind.PurgeFolder, folder, recycledETag);

    public static VaultChange AddShareGrant(ShareGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new(VaultChangeKind.AddShareGrant, grant.ItemId, null, grant: grant);
    }

    public static VaultChange ReplaceShareGrant(ShareGrant grant, string byItemETag, string byRecipientETag) =>
        ForGrant(VaultChangeKind.ReplaceShareGrant, grant, byItemETag, byRecipientETag);

    public static VaultChange DeleteShareGrant(ShareGrant grant, string byItemETag, string byRecipientETag) =>
        ForGrant(VaultChangeKind.DeleteShareGrant, grant, byItemETag, byRecipientETag);

    public static VaultChange DeleteShareGrantByItem(ShareGrant grant, string byItemETag)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(byItemETag);
        return new(VaultChangeKind.DeleteShareGrantByItem, grant.ItemId, byItemETag, grant: grant);
    }

    public static VaultChange RemoveSecret(Guid secretId, string etag) => ForRemoval(VaultChangeKind.RemoveSecret, secretId, etag);

    public static VaultChange RemoveRecycledSecret(Guid secretId, string etag) => ForRemoval(VaultChangeKind.RemoveRecycledSecret, secretId, etag);

    public static VaultChange RemoveFolder(Guid folderId, string etag) => ForRemoval(VaultChangeKind.RemoveFolder, folderId, etag);

    public static VaultChange RemoveRecycledFolder(Guid folderId, string etag) => ForRemoval(VaultChangeKind.RemoveRecycledFolder, folderId, etag);

    public static VaultChange TouchFolder(Guid folderId, string etag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        return new(VaultChangeKind.TouchFolder, folderId, etag);
    }

    public static VaultChange TouchKeyRing(string etag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        return new(VaultChangeKind.TouchKeyRing, Guid.Empty, etag);
    }

    private static VaultChange ForRemoval(VaultChangeKind kind, Guid itemId, string etag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        return new(kind, itemId, etag);
    }

    private static void RequireDeleted(DateTimeOffset? deletedAt, Guid? deletionBatchId)
    {
        if (deletedAt is null || deletionBatchId is null)
        {
            throw new ArgumentException("A recycled item must have DeletedAt and DeletionBatchId set.");
        }
    }

    private static void RequireNotDeleted(DateTimeOffset? deletedAt, Guid? deletionBatchId)
    {
        if (deletedAt is not null || deletionBatchId is not null)
        {
            throw new ArgumentException("A restored item must have DeletedAt and DeletionBatchId cleared.");
        }
    }

    private static VaultChange ForSecret(VaultChangeKind kind, Secret secret, string etag)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        return new(kind, secret.Id, etag, secret: secret);
    }

    private static VaultChange ForGrant(VaultChangeKind kind, ShareGrant grant, string byItemETag, string byRecipientETag)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(byItemETag);
        ArgumentException.ThrowIfNullOrWhiteSpace(byRecipientETag);
        return new(kind, grant.ItemId, byItemETag, grant: grant, secondaryETag: byRecipientETag);
    }

    private static VaultChange ForFolder(VaultChangeKind kind, Folder folder, string etag)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        return new(kind, folder.Id, etag, folder: folder);
    }
}
