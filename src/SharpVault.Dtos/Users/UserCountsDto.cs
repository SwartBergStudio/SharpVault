// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Users;

public sealed class UserCountsDto
{
    public int ItemCount { get; set; }

    public int ShareCount { get; set; }

    public bool CountsAreCapped { get; set; }
}
