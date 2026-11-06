// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Common;

public static class VaultEnumMap
{
    public static SecretType ToDomain(SecretTypeDto type) => type switch
    {
        SecretTypeDto.Login => SecretType.Login,
        SecretTypeDto.SecureNote => SecretType.SecureNote,
        SecretTypeDto.ApiKey => SecretType.ApiKey,
        SecretTypeDto.Card => SecretType.Card,
        SecretTypeDto.Other => SecretType.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static SecretTypeDto ToDto(SecretType type) => type switch
    {
        SecretType.Login => SecretTypeDto.Login,
        SecretType.SecureNote => SecretTypeDto.SecureNote,
        SecretType.ApiKey => SecretTypeDto.ApiKey,
        SecretType.Card => SecretTypeDto.Card,
        SecretType.Other => SecretTypeDto.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
