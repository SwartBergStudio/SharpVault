// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Maintenance;

public sealed class MaintenanceLease
{
    public string Name { get; set; } = string.Empty;

    public string Holder { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? RunId { get; set; }

    public Guid? AfterUserId { get; set; }

    public DateTimeOffset? RunStartedAt { get; set; }

    public DateTimeOffset? LastCompletedAt { get; set; }
}
