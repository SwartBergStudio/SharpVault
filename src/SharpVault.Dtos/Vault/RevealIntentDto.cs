// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Serialization;

namespace SharpVault.Dtos.Vault;

[JsonConverter(typeof(StrictEnumJsonConverter<RevealIntentDto>))]
public enum RevealIntentDto
{
    View = 1,
    Copy = 2,
}
