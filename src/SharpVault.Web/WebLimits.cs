// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web;

internal static class WebLimits
{
    public const int MaxHubMessageBytes = 64 * 1024;

    public static readonly TimeSpan SharedRevokeTimeout = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan CircuitOpenCheckTimeout = TimeSpan.FromSeconds(3);
}
