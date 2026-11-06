// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Serialization;

namespace SharpVault.Dtos.Transfers;

[JsonConverter(typeof(StrictEnumJsonConverter<TransferScopeKindDto>))]
public enum TransferScopeKindDto
{
    AllItems = 1,
    SelectedFolders = 2,
}
