// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Users;

public sealed class SessionReferenceRequestDto
{
    public string SessionId { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
