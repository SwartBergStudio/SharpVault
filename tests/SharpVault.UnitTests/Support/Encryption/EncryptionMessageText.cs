// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Encryption;

internal static class EncryptionMessageText
{
    public const string InvalidPurpose = "The encryption purpose must be 1 to 64 lowercase letters, digits, dots or hyphens and start with a letter.";

    public const string EmptyContextId = "The encryption context needs a non-empty item id and owner id.";

    public const string PlaintextTooLarge = "The value is too large to encrypt.";

    public const string PlaintextNotValidUnicode = "The value is not valid Unicode text.";

    public const string InvalidDataKeyLength = "Data keys must be 32 bytes.";

    public const string UnknownProvider = "Encryption:Provider must be KeyVault or Development.";

    public const string DevelopmentProviderNotAllowed = "The development key wrap provider can only run in the Development environment.";

    public const string DevelopmentKeyMaterialMissing = "The development key wrap provider needs Encryption:DevelopmentKeyMaterial with at least 32 characters.";

    public const string DevelopmentKeyMaterialNotAllowed = "Encryption:DevelopmentKeyMaterial must not be set outside the Development environment.";

    public const string KeyVaultKeyIdInvalid = "Encryption:KeyVaultKeyId must be an absolute https Key Vault key URI without a version, query string or user information.";

    public const string KeyVaultReturnedUnexpectedKey = "Key Vault wrapped the data key with an unexpected key.";

    public const string SecretDecryption = "The encrypted value could not be decrypted.";

    public static string ForParameter(string message, string parameterName) => $"{message} (Parameter '{parameterName}')";
}
