// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.OneTimeLinks;

public sealed class OneTimeLinkNoticeListDto
{
    public List<OneTimeLinkNoticeDto> Items { get; set; } = [];
}
