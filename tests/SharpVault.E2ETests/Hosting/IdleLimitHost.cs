// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.E2ETests.Hosting;

public sealed class IdleLimitHost : E2EHost
{
    public const string IdleTimeoutSetting = "IdleLock__Timeout";
    public const string OneMinute = "00:01:00";

    protected override IReadOnlyDictionary<string, string> ExtraWebSettings { get; } = new Dictionary<string, string>
    {
        [IdleTimeoutSetting] = OneMinute,
    };
}
