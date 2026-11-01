// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Users;

public sealed class User
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public UserStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSignInAt { get; set; }
}
