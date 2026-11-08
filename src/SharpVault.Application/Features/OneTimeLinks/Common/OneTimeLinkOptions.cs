// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed class OneTimeLinkOptions
{
    public const string SectionName = "OneTimeLinks";

    public bool Enabled { get; set; }

    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromDays(7);

    public bool PurgeEnabled { get; set; } = true;

    public TimeSpan PurgeInitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan PurgeLeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
}
