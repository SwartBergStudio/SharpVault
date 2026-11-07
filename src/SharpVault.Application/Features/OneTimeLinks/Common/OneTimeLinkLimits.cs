// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Access;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public static class OneTimeLinkLimits
{
    public const int MinLifetimeMinutes = 5;

    public const int MaxActiveLinksPerCreator = 100;

    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    public const int NoticeListLimit = 20;

    public const int MaxPayloadBytes = 131072;

    public const int UserAgentMaxLength = UserProfileText.UserAgentMaxLength;

    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan AbsoluteMaxLifetime = TimeSpan.FromDays(7);
}
