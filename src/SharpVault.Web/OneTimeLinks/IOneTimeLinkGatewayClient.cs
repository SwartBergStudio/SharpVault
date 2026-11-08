// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.OneTimeLinks;

public interface IOneTimeLinkGatewayClient
{
    Task<OneTimeLinkOpenResult> OpenAsync(Guid linkId, string clientIp, string userAgent, CancellationToken cancellationToken);
}
