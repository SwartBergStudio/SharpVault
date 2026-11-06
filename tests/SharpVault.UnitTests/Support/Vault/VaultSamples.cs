// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Support.Vault;

internal static class VaultSamples
{
    public static readonly DateTimeOffset Created = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public static byte[] Bytes(int length) => RandomNumberGenerator.GetBytes(length);

    public static WrappedDataKey Key() => new("key-id-1", "RSA-OAEP-256", Bytes(48));

    public static Secret Secret(Guid owner, Guid? id = null, Guid? folderId = null, int nameLength = 40) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OwnerId = owner,
        Type = SecretType.Login,
        FolderId = folderId,
        NameEnvelope = Bytes(nameLength),
        UsernameEnvelope = Bytes(30),
        UrlEnvelope = Bytes(31),
        TagsEnvelope = Bytes(32),
        ValueEnvelope = Bytes(33),
        NotesEnvelope = Bytes(34),
        DataKey = Key(),
        MetadataKeyVersion = 1,
        Revision = 3,
        CreatedAt = Created,
        ModifiedAt = Created.AddMinutes(5),
    };

    public static Secret Recycled(Guid owner, Guid? id = null, Guid? batch = null)
    {
        var secret = Secret(owner, id);
        secret.DeletedAt = Created.AddDays(1);
        secret.DeletionBatchId = batch ?? Guid.NewGuid();
        return secret;
    }

    public static Folder Folder(Guid owner, Guid? id = null, Guid? parentId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OwnerId = owner,
        ParentId = parentId,
        NameEnvelope = Bytes(40),
        MetadataKeyVersion = 1,
        CreatedAt = Created,
        ModifiedAt = Created.AddMinutes(1),
    };

    public static Folder RecycledFolder(Guid owner, Guid? id = null, Guid? parentId = null, Guid? batch = null)
    {
        var folder = Folder(owner, id, parentId);
        folder.DeletedAt = Created.AddDays(1);
        folder.DeletionBatchId = batch ?? Guid.NewGuid();
        return folder;
    }

    public static SecretVersion Version(Guid owner, Guid secretId, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        SecretId = secretId,
        OwnerId = owner,
        ValueEnvelope = Bytes(60),
        Revision = 2,
        ReplacedAt = Created.AddHours(1),
    };

    public static VaultKeyRing KeyRing(Guid owner) => new()
    {
        OwnerId = owner,
        MetadataKey = Key(),
        MetadataKeyVersion = 1,
        CreatedAt = Created,
    };
}
