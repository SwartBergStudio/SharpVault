// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Maintenance;

public sealed class RecycleBinPurgeOptions
{
    public const string SectionName = "RecycleBinPurge";

    public bool Enabled { get; set; } = true;

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
}
