// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Api.Security;

public static class LinkGatewayMessages
{
    public const string ProofRejected = "The link gateway proof was rejected.";

    public const string LinksNeedGatewayKey = "OneTimeLinks:Enabled needs WebGateway:Key of at least 32 characters. The value is not shown.";
}
