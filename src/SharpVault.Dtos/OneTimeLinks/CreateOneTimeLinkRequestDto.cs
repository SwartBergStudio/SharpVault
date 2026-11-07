// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;

namespace SharpVault.Dtos.OneTimeLinks;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateOneTimeLinkRequestDto
{
    public string Payload { get; set; } = string.Empty;

    public int? LifetimeMinutes { get; set; }
}
