// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Serialization;

namespace SharpVault.Dtos.Transfers;

[JsonConverter(typeof(StrictEnumJsonConverter<TransferBlockReasonDto>))]
public enum TransferBlockReasonDto
{
    RequesterNoLongerAdmin = 1,
    ApproverNoLongerAdmin = 2,
    SourceMissing = 3,
    SourceNotLocked = 4,
    TargetMissing = 5,
    TargetLocked = 6,
    TargetFolderLimit = 7,
    SourceVaultUnreadable = 8,
    TargetVaultUnreadable = 9,
}
