// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.E2ETests.Hosting;

public sealed class SharingLimitHost : E2EHost
{
    public const string ShareWritesSetting = "VaultRateLimits__ShareWritesPerMinute";
    public const string OneShareWritePerMinute = "1";

    protected override IReadOnlyDictionary<string, string> ExtraApiSettings { get; } = new Dictionary<string, string>
    {
        [ShareWritesSetting] = OneShareWritePerMinute,
    };
}
