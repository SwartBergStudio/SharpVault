// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Vault;

public sealed class SecretPageDto
{
    public List<SecretDto> Items { get; set; } = [];

    public string? ContinuationToken { get; set; }
}
