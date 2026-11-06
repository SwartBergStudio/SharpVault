// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Queries;

public sealed record ListTransferNoticesQuery : IRequest<Result<TransferNoticeListDto>>;

public sealed class ListTransferNoticesQueryHandler(ICurrentUser currentUser, ITransferJobStore jobs)
    : IRequestHandler<ListTransferNoticesQuery, Result<TransferNoticeListDto>>
{
    public async Task<Result<TransferNoticeListDto>> Handle(ListTransferNoticesQuery request, CancellationToken cancellationToken)
    {
        var notices = await jobs.ListNoticesAsync(currentUser.UserId, TransferLimits.NoticeListLimit, cancellationToken);
        return Result<TransferNoticeListDto>.Success(new TransferNoticeListDto
        {
            Items = notices.Select(TransferDtoFactory.ToNotice).ToList(),
        });
    }
}
