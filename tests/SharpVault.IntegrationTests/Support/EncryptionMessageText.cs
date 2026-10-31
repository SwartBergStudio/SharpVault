// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Support;

public static class EncryptionMessageText
{
    public const string DevelopmentProviderNotAllowed = "The development key wrap provider can only run in the Development environment.";

    public const string DevelopmentKeyMaterialMissing = "The development key wrap provider needs Encryption:DevelopmentKeyMaterial with at least 32 characters.";

    public const string DevelopmentKeyMaterialNotAllowed = "Encryption:DevelopmentKeyMaterial must not be set outside the Development environment.";

    public const string KeyVaultKeyIdInvalid = "Encryption:KeyVaultKeyId must be an absolute https Key Vault key URI without a version, query string or user information.";

    public const string UnknownProvider = "Encryption:Provider must be KeyVault or Development.";

    public const string DevelopmentWarning = "Data keys are wrapped by the development key wrap provider, which is only allowed in the Development environment";

    public const string KeyVaultInUse = "Data keys are wrapped by KeyVaultKeyWrapProvider";
}
