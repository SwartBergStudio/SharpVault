// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos;

namespace SharpVault.Web.OneTimeLinks;

public static class OneTimeLinkRelayRules
{
    public const string LinkOpenHeader = "X-SharpVault-Link-Open";

    public const string LinkOpenHeaderValue = "1";

    public const string RateLimitedStatus = "rate-limited";

    public const string UnavailableStatus = "unavailable";

    public const int UserAgentMaxLength = 512;

    public const string LinkIdFormat = "D";

    private const string LinkIdToken = "{linkId:guid}";

    public static string OpenPath(Guid linkId) =>
        ApiRoutes.OneTimeLinkOpen.Replace(LinkIdToken, linkId.ToString(LinkIdFormat), StringComparison.Ordinal);
}
