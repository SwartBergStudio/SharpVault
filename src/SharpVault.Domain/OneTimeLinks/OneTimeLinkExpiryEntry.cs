// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.OneTimeLinks;

public sealed class OneTimeLinkExpiryEntry
{
    public Guid LinkId { get; set; }

    public Guid CreatorId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
