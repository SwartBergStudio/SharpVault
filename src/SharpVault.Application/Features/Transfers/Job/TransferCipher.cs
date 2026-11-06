// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Transfers.Job;

public static class TransferCipher
{
    private const string UnreadableFolderName = "Unreadable folder";

    public sealed record RenamedSecret(Secret Secret, IReadOnlyList<SecretVersion> Versions);

    public static Secret Skeleton(Secret source, Guid targetOwnerId, Guid? targetFolderId) => new()
    {
        Id = source.Id,
        OwnerId = targetOwnerId,
        Type = source.Type,
        FolderId = targetFolderId,
        NameEnvelope = source.NameEnvelope,
        UsernameEnvelope = source.UsernameEnvelope,
        UrlEnvelope = source.UrlEnvelope,
        TagsEnvelope = source.TagsEnvelope,
        ValueEnvelope = source.ValueEnvelope,
        NotesEnvelope = source.NotesEnvelope,
        DataKey = source.DataKey,
        MetadataKeyVersion = source.MetadataKeyVersion,
        Revision = source.Revision,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
        DeletedAt = source.DeletedAt,
        DeletionBatchId = source.DeletionBatchId,
    };

    public static void RebindMetadata(
        IDataKeySession sourceSession,
        IDataKeySession targetSession,
        Secret from,
        Secret to,
        int targetMetadataKeyVersion)
    {
        to.NameEnvelope = MoveMetadata(sourceSession, targetSession, SecretContext(VaultPurposes.SecretName, from), SecretContext(VaultPurposes.SecretName, to), from.NameEnvelope);
        to.UsernameEnvelope = MoveOptionalMetadata(sourceSession, targetSession, from, to, VaultPurposes.SecretUsername, from.UsernameEnvelope);
        to.UrlEnvelope = MoveOptionalMetadata(sourceSession, targetSession, from, to, VaultPurposes.SecretUrl, from.UrlEnvelope);
        to.TagsEnvelope = MoveOptionalMetadata(sourceSession, targetSession, from, to, VaultPurposes.SecretTags, from.TagsEnvelope);
        to.MetadataKeyVersion = targetMetadataKeyVersion;
    }

    public static IReadOnlyList<SecretVersion> RebindContent(
        IDataKeySession secretSession,
        Secret from,
        Secret to,
        IReadOnlyList<SecretVersion> versions)
    {
        to.ValueEnvelope = MoveOptionalContent(secretSession, SecretContext(VaultPurposes.SecretValue, from), SecretContext(VaultPurposes.SecretValue, to), from.ValueEnvelope);
        to.NotesEnvelope = MoveOptionalContent(secretSession, SecretContext(VaultPurposes.SecretNotes, from), SecretContext(VaultPurposes.SecretNotes, to), from.NotesEnvelope);

        return versions.Select(version => RebindVersion(secretSession, from, to, version)).ToList();
    }

    public static Folder RebindFolder(
        IDataKeySession sourceSession,
        IDataKeySession targetSession,
        Folder from,
        Guid targetOwnerId,
        Guid? parentId,
        int targetMetadataKeyVersion)
    {
        var to = NewFolder(from, targetOwnerId, parentId, targetMetadataKeyVersion);
        try
        {
            to.NameEnvelope = MoveMetadata(sourceSession, targetSession, FolderContext(from), FolderContext(to), from.NameEnvelope);
        }
        catch (SecretDecryptionException)
        {
            SecretMetadataCipher.EncryptFolderName(targetSession, to, UnreadableFolderName);
        }

        return to;
    }

    public static RenamedSecret Rename(
        IDataKeySession metadataSession,
        IDataKeySession contentSession,
        Secret staged,
        Guid newSecretId,
        Guid rootFolderId,
        int targetMetadataKeyVersion,
        IReadOnlyList<SecretVersion> versions)
    {
        var renamed = Skeleton(staged, staged.OwnerId, rootFolderId);
        renamed.Id = newSecretId;
        RebindMetadata(metadataSession, metadataSession, staged, renamed, targetMetadataKeyVersion);
        var moved = RebindContent(contentSession, staged, renamed, versions);
        foreach (var version in moved)
        {
            version.SecretId = newSecretId;
        }

        return new RenamedSecret(renamed, moved);
    }

    public static Folder BuildRootFolder(
        IDataKeySession targetSession,
        TransferJob job,
        Guid rootFolderId,
        int targetMetadataKeyVersion,
        DateTimeOffset now)
    {
        var root = new Folder
        {
            Id = rootFolderId,
            OwnerId = job.TargetUserId,
            ParentId = null,
            MetadataKeyVersion = targetMetadataKeyVersion,
            CreatedAt = now,
            ModifiedAt = now,
        };

        SecretMetadataCipher.EncryptFolderName(targetSession, root, TransferFolderName.Build(job.SourceDisplayName, job.DecidedAt ?? job.RequestedAt));
        return root;
    }

    private static Folder NewFolder(Folder from, Guid targetOwnerId, Guid? parentId, int targetMetadataKeyVersion) => new()
    {
        Id = from.Id,
        OwnerId = targetOwnerId,
        ParentId = parentId,
        NameEnvelope = from.NameEnvelope,
        MetadataKeyVersion = targetMetadataKeyVersion,
        CreatedAt = from.CreatedAt,
        ModifiedAt = from.ModifiedAt,
        DeletedAt = from.DeletedAt,
        DeletionBatchId = from.DeletionBatchId,
    };

    private static SecretVersion RebindVersion(IDataKeySession session, Secret from, Secret to, SecretVersion version) => new()
    {
        Id = version.Id,
        SecretId = version.SecretId,
        OwnerId = to.OwnerId,
        ValueEnvelope = MoveContent(
            session,
            new EncryptionContext(VaultPurposes.SecretValueHistory, version.Id, from.OwnerId),
            new EncryptionContext(VaultPurposes.SecretValueHistory, version.Id, to.OwnerId),
            version.ValueEnvelope),
        Revision = version.Revision,
        ReplacedAt = version.ReplacedAt,
    };

    private static byte[]? MoveOptionalMetadata(IDataKeySession sourceSession, IDataKeySession targetSession, Secret from, Secret to, string purpose, byte[]? envelope) =>
        envelope is null
            ? null
            : MoveMetadata(sourceSession, targetSession, SecretContext(purpose, from), SecretContext(purpose, to), envelope);

    private static byte[]? MoveOptionalContent(IDataKeySession session, EncryptionContext source, EncryptionContext target, byte[]? envelope) =>
        envelope is null ? null : MoveContent(session, source, target, envelope);

    private static byte[] MoveMetadata(IDataKeySession sourceSession, IDataKeySession targetSession, EncryptionContext source, EncryptionContext target, byte[] envelope)
    {
        var moved = sourceSession.ReEncryptTo(source, targetSession, target, envelope);
        Verify(targetSession, target, moved);
        return moved;
    }

    private static byte[] MoveContent(IDataKeySession session, EncryptionContext source, EncryptionContext target, byte[] envelope)
    {
        var moved = session.ReEncrypt(source, target, envelope);
        Verify(session, target, moved);
        return moved;
    }

    private static void Verify(IDataKeySession session, EncryptionContext context, byte[] envelope)
    {
        var proof = session.ReEncrypt(context, context, envelope);
        CryptographicOperations.ZeroMemory(proof);
    }

    private static EncryptionContext SecretContext(string purpose, Secret secret) => new(purpose, secret.Id, secret.OwnerId);

    private static EncryptionContext FolderContext(Folder folder) => new(VaultPurposes.FolderName, folder.Id, folder.OwnerId);
}
