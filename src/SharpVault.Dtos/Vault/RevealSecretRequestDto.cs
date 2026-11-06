// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;

namespace SharpVault.Dtos.Vault;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RevealSecretRequestDto
{
    public RevealFieldDto Field { get; set; }

    public RevealIntentDto Intent { get; set; }
}
