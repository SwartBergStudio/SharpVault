// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Api.RateLimiting;

public sealed class VaultRateLimitOptions
{
    public const string SectionName = "VaultRateLimits";

    public int RequestsPerMinute { get; set; } = 600;

    public int RevealsPerMinute { get; set; } = 60;

    public int ShareWritesPerMinute { get; set; } = 120;

    public int DirectorySearchesPerMinute { get; set; } = 30;

    public int AdminActionsPerMinute { get; set; } = 120;

    public int OneTimeLinkOpensPerMinute { get; set; } = 20;
}
