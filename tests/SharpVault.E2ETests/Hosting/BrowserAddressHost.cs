// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.E2ETests.Hosting;

public sealed class BrowserAddressHost : E2EHost
{
    private const string ApiBaseAddressSetting = "SharpVaultApi__BaseAddress";
    private const string LocalhostName = "localhost";
    private const string LoopbackIpv4 = "127.0.0.1";
    private const string LoopbackIpv6 = "[::1]";

    protected override IReadOnlyDictionary<string, string> ExtraWebSettings =>
        new Dictionary<string, string>
        {
            [ApiBaseAddressSetting] = ApiUrl.Replace(LocalhostName, LoopbackIpv4, StringComparison.Ordinal) + "/",
        };

    public string Ipv6WebUrl => WebUrl.Replace(LocalhostName, LoopbackIpv6, StringComparison.Ordinal);
}
