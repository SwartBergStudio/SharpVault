// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;

namespace SharpVault.Dtos.Transfers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransferDecisionRequestDto
{
    public string Reason { get; set; } = string.Empty;
}
