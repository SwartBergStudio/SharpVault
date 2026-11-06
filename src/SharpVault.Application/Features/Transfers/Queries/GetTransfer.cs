// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Queries;

public sealed record GetTransferQuery(Guid TransferId) : IRequest<Result<TransferDto>>;

public sealed class GetTransferQueryValidator : AbstractValidator<GetTransferQuery>
{
    public GetTransferQueryValidator()
    {
        RuleFor(x => x.TransferId).NotEqual(Guid.Empty);
    }
}

public sealed class GetTransferQueryHandler(ICurrentUser currentUser, ITransferJobStore jobs)
    : IRequestHandler<GetTransferQuery, Result<TransferDto>>
{
    public async Task<Result<TransferDto>> Handle(GetTransferQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdmin)
        {
            return Result<TransferDto>.Forbidden();
        }

        var stored = await jobs.GetAsync(request.TransferId, cancellationToken);
        if (stored is null || TransferVisibility.IsHiddenFrom(stored.Value, currentUser.UserId))
        {
            return Result<TransferDto>.NotFound();
        }

        return Result<TransferDto>.Success(TransferDtoFactory.ToDto(stored, currentUser.UserId));
    }
}
