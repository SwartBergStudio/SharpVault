// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Serialization;

namespace SharpVault.Dtos.Transfers;

[JsonConverter(typeof(StrictEnumJsonConverter<TransferNoticeKindDto>))]
public enum TransferNoticeKindDto
{
    Received = 1,
    RequestCompleted = 2,
    ApprovalCompleted = 3,
}
