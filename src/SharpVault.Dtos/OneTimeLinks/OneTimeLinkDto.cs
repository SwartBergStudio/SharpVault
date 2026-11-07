// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.OneTimeLinks;

public sealed class OneTimeLinkDto
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid ItemId { get; set; }

    public string? ItemName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
