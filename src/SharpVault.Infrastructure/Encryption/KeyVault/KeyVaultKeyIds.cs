// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using Azure.Security.KeyVault.Keys;

namespace SharpVault.Infrastructure.Encryption.KeyVault;

public static class KeyVaultKeyIds
{
    public const int MaxKeyIdLength = 512;
    private const string KeysPathPrefix = "/keys/";

    public static bool TryParseConfigured(string? keyId, [NotNullWhen(true)] out KeyVaultKeyIdentifier? identifier)
    {
        if (TryParse(keyId, out var parsed) && parsed.Version is null)
        {
            identifier = parsed;
            return true;
        }

        identifier = null;
        return false;
    }

    public static bool IsVersionOf(string? keyId, KeyVaultKeyIdentifier configured)
    {
        if (!TryParse(keyId, out var parsed) || string.IsNullOrEmpty(parsed.Version))
        {
            return false;
        }

        return Uri.Compare(parsed.VaultUri, configured.VaultUri, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0
            && string.Equals(parsed.Name, configured.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParse(string? keyId, out KeyVaultKeyIdentifier identifier)
    {
        identifier = default;

        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > MaxKeyIdLength)
        {
            return false;
        }

        if (!Uri.TryCreate(keyId, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0
            || !uri.AbsolutePath.StartsWith(KeysPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return KeyVaultKeyIdentifier.TryCreate(uri, out identifier);
    }
}
