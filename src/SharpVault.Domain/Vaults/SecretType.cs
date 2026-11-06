// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Vaults;

public enum SecretType
{
    Login = 1,
    SecureNote = 2,
    ApiKey = 3,
    Card = 4,
    Other = 5,
}
