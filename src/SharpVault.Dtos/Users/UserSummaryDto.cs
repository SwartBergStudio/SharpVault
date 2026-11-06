// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Users;

public sealed class UserSummaryDto
{
    public Guid UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public UserStatusDto Status { get; set; }

    public DateTimeOffset LastSignInAt { get; set; }
}
