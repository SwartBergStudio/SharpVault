// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Serialization;

namespace SharpVault.Dtos.Transfers;

[JsonConverter(typeof(StrictEnumJsonConverter<TransferStateDto>))]
public enum TransferStateDto
{
    PendingApproval = 1,
    Rejected = 2,
    Expired = 3,
    Approved = 4,
    Staging = 5,
    Blocked = 6,
    Aborted = 7,
    Releasing = 8,
    Publishing = 9,
    Reassigning = 10,
    Completed = 11,
}
