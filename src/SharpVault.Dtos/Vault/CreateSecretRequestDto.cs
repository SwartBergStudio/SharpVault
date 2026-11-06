// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;

namespace SharpVault.Dtos.Vault;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateSecretRequestDto
{
    public SecretTypeDto Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? Url { get; set; }

    public string? Value { get; set; }

    public string? Notes { get; set; }

    public List<string>? Tags { get; set; }

    public Guid? FolderId { get; set; }
}
