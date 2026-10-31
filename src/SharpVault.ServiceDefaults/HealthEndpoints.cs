// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.ServiceDefaults;

public static class HealthEndpoints
{
    public const string HealthPath = "/health";
    public const string AlivePath = "/alive";
    public const string LiveTag = "live";
    public const string SelfCheckName = "self";
    public const string CachePolicyName = "health";

    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
}
