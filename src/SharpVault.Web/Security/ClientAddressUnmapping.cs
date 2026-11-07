// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;

namespace SharpVault.Web.Security;

internal static class ClientAddressUnmapping
{
    internal static IPAddress Unmap(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
