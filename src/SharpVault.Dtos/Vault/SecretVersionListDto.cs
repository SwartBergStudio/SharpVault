// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Vault;

public sealed class SecretVersionListDto
{
    public List<SecretVersionDto> Items { get; set; } = [];
}
