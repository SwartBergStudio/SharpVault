// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Security.KeyVault.Keys.Cryptography;

namespace SharpVault.Infrastructure.Encryption.KeyVault;

public interface IKeyVaultCryptographyClientFactory
{
    CryptographyClient GetClient(Uri keyId);
}
