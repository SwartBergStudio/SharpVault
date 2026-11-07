// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.Common;

public static class SecretMetadataCipher
{
    public static void Encrypt(IDataKeySession metadataSession, Secret secret, SecretMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadataSession);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(metadata);

        secret.NameEnvelope = metadataSession.Encrypt(Context(VaultPurposes.SecretName, secret), metadata.Name.Trim());
        secret.UsernameEnvelope = EncryptOptional(metadataSession, Context(VaultPurposes.SecretUsername, secret), metadata.Username);
        secret.UrlEnvelope = EncryptOptional(metadataSession, Context(VaultPurposes.SecretUrl, secret), metadata.Url);
        secret.TagsEnvelope = metadata.Tags.Count == 0
            ? null
            : metadataSession.Encrypt(
                Context(VaultPurposes.SecretTags, secret),
                JsonSerializer.Serialize(metadata.Tags.Select(tag => tag.Trim()).ToList()));
    }

    public static bool TryDecrypt(
        IDataKeySession metadataSession,
        Secret secret,
        int expectedVersion,
        [NotNullWhen(true)] out SecretMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(metadataSession);
        ArgumentNullException.ThrowIfNull(secret);

        metadata = null;
        if (secret.MetadataKeyVersion != expectedVersion)
        {
            return false;
        }

        try
        {
            var name = metadataSession.Decrypt(Context(VaultPurposes.SecretName, secret), secret.NameEnvelope);
            var username = DecryptOptional(metadataSession, Context(VaultPurposes.SecretUsername, secret), secret.UsernameEnvelope);
            var url = DecryptOptional(metadataSession, Context(VaultPurposes.SecretUrl, secret), secret.UrlEnvelope);
            var tags = DecryptTags(metadataSession, secret);
            if (tags is null)
            {
                return false;
            }

            metadata = new SecretMetadata(name, username, url, tags);
            return true;
        }
        catch (SecretDecryptionException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryDecryptName(
        IDataKeySession metadataSession,
        Guid secretId,
        Guid ownerId,
        byte[] envelope,
        int storedVersion,
        int expectedVersion,
        [NotNullWhen(true)] out string? name)
    {
        ArgumentNullException.ThrowIfNull(metadataSession);
        ArgumentNullException.ThrowIfNull(envelope);

        name = null;
        if (storedVersion != expectedVersion)
        {
            return false;
        }

        try
        {
            name = metadataSession.Decrypt(new EncryptionContext(VaultPurposes.SecretName, secretId, ownerId), envelope);
            return true;
        }
        catch (SecretDecryptionException)
        {
            return false;
        }
    }

    public static void EncryptFolderName(IDataKeySession metadataSession, Folder folder, string name)
    {
        ArgumentNullException.ThrowIfNull(metadataSession);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        folder.NameEnvelope = metadataSession.Encrypt(new EncryptionContext(VaultPurposes.FolderName, folder.Id, folder.OwnerId), name.Trim());
    }

    public static bool TryDecryptFolderName(
        IDataKeySession metadataSession,
        Folder folder,
        int expectedVersion,
        [NotNullWhen(true)] out string? name)
    {
        ArgumentNullException.ThrowIfNull(metadataSession);
        ArgumentNullException.ThrowIfNull(folder);

        name = null;
        if (folder.MetadataKeyVersion != expectedVersion)
        {
            return false;
        }

        try
        {
            name = metadataSession.Decrypt(new EncryptionContext(VaultPurposes.FolderName, folder.Id, folder.OwnerId), folder.NameEnvelope);
            return true;
        }
        catch (SecretDecryptionException)
        {
            return false;
        }
    }

    private static EncryptionContext Context(string purpose, Secret secret) => new(purpose, secret.Id, secret.OwnerId);

    private static byte[]? EncryptOptional(IDataKeySession session, EncryptionContext context, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : session.Encrypt(context, value.Trim());

    private static string? DecryptOptional(IDataKeySession session, EncryptionContext context, byte[]? envelope) =>
        envelope is null ? null : session.Decrypt(context, envelope);

    private static List<string>? DecryptTags(IDataKeySession session, Secret secret)
    {
        if (secret.TagsEnvelope is null)
        {
            return [];
        }

        var json = session.Decrypt(Context(VaultPurposes.SecretTags, secret), secret.TagsEnvelope);
        var tags = JsonSerializer.Deserialize<List<string?>>(json);
        return tags is null || tags.Any(tag => tag is null) ? null : [.. tags.OfType<string>()];
    }
}
