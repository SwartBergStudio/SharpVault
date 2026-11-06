// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Users;

public sealed class CurrentUserDto
{
    public Guid UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
}
