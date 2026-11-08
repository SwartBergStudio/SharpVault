// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Queries;

public sealed record ListOneTimeLinkNoticesQuery : IRequest<Result<OneTimeLinkNoticeListDto>>;

public sealed class ListOneTimeLinkNoticesQueryHandler(IOneTimeLinkStore links, VaultKeyRingService keyRings, ICurrentUser currentUser)
    : IRequestHandler<ListOneTimeLinkNoticesQuery, Result<OneTimeLinkNoticeListDto>>
{
    public async Task<Result<OneTimeLinkNoticeListDto>> Handle(ListOneTimeLinkNoticesQuery request, CancellationToken cancellationToken)
    {
        var notices = await links.ListNoticesAsync(currentUser.UserId, OneTimeLinkLimits.NoticeListLimit, cancellationToken);

        using var names = new OwnerVaultReads(keyRings);
        var items = new List<OneTimeLinkNoticeDto>(notices.Count);
        foreach (var notice in notices)
        {
            items.Add(await ToDtoAsync(notice, names, cancellationToken));
        }

        return Result<OneTimeLinkNoticeListDto>.Success(new OneTimeLinkNoticeListDto { Items = items });
    }

    private static async Task<OneTimeLinkNoticeDto> ToDtoAsync(OneTimeLinkNotice notice, OwnerVaultReads names, CancellationToken cancellationToken)
    {
        var secretName = await names.TryNameAsync(notice.OwnerId, notice.SecretId, notice.NameEnvelope, notice.MetadataKeyVersion, cancellationToken);
        return new OneTimeLinkNoticeDto
        {
            NoticeId = notice.NoticeId,
            LinkId = notice.LinkId,
            ItemName = secretName,
            OpenedAt = notice.OpenedAt,
        };
    }
}
