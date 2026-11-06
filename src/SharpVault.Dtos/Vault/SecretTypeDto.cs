// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Serialization;

namespace SharpVault.Dtos.Vault;

[JsonConverter(typeof(StrictEnumJsonConverter<SecretTypeDto>))]
public enum SecretTypeDto
{
    Login = 1,
    SecureNote = 2,
    ApiKey = 3,
    Card = 4,
    Other = 5,
}
