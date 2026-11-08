// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Authentication.WebGateway;

public static class WebGatewayHeaders
{
    public const string Time = "X-SharpVault-Gateway-Time";
    public const string Signature = "X-SharpVault-Gateway-Signature";
    public const string ClientIp = "X-SharpVault-Client-Ip";
    public const string ClientAgent = "X-SharpVault-Client-Agent";
}
