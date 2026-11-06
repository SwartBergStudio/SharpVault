// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.Common;

public static class SecretValueCipher
{
    public static byte[]? EncryptValue(IDataKeySession secretKey, Secret secret, string? value) =>
        Encrypt(secretKey, ValueContext(secret), value);

    public static byte[]? EncryptNotes(IDataKeySession secretKey, Secret secret, string? notes) =>
        Encrypt(secretKey, NotesContext(secret), notes);

    public static string DecryptValue(IDataKeySession secretKey, Secret secret, byte[] envelope) =>
        secretKey.Decrypt(ValueContext(secret), envelope);

    public static string DecryptNotes(IDataKeySession secretKey, Secret secret, byte[] envelope) =>
        secretKey.Decrypt(NotesContext(secret), envelope);

    public static string DecryptVersion(IDataKeySession secretKey, Secret secret, SecretVersion version) =>
        secretKey.Decrypt(new EncryptionContext(VaultPurposes.SecretValueHistory, version.Id, secret.OwnerId), version.ValueEnvelope);

    public static byte[] PromoteVersion(IDataKeySession secretKey, Secret secret, SecretVersion version)
    {
        ArgumentNullException.ThrowIfNull(secretKey);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(version);

        return secretKey.ReEncrypt(
            new EncryptionContext(VaultPurposes.SecretValueHistory, version.Id, secret.OwnerId),
            ValueContext(secret),
            version.ValueEnvelope);
    }

    private static byte[]? Encrypt(IDataKeySession secretKey, EncryptionContext context, string? text)
    {
        ArgumentNullException.ThrowIfNull(secretKey);

        return text is null ? null : secretKey.Encrypt(context, text);
    }

    private static EncryptionContext ValueContext(Secret secret) => new(VaultPurposes.SecretValue, secret.Id, secret.OwnerId);

    private static EncryptionContext NotesContext(Secret secret) => new(VaultPurposes.SecretNotes, secret.Id, secret.OwnerId);
}
