// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.OneTimeLinks;

public sealed class OneTimeLinkNoticeDto
{
    public string NoticeId { get; set; } = string.Empty;

    public Guid LinkId { get; set; }

    public string? ItemName { get; set; }

    public DateTimeOffset OpenedAt { get; set; }
}
