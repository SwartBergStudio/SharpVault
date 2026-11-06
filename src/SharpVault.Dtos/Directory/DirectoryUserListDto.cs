// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Directory;

public sealed class DirectoryUserListDto
{
    public List<DirectoryUserDto> Items { get; set; } = [];
}
