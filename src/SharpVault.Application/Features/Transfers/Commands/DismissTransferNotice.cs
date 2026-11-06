// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Features.Transfers.Commands;

public sealed record DismissTransferNoticeCommand(Guid TransferId) : IRequest<Result<Done>>;

public sealed class DismissTransferNoticeCommandValidator : AbstractValidator<DismissTransferNoticeCommand>
{
    public DismissTransferNoticeCommandValidator()
    {
        RuleFor(x => x.TransferId).NotEqual(Guid.Empty);
    }
}

public sealed class DismissTransferNoticeCommandHandler(ITransferJobStore jobs, ICurrentUser currentUser)
    : IRequestHandler<DismissTransferNoticeCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(DismissTransferNoticeCommand request, CancellationToken cancellationToken)
    {
        var outcome = await jobs.DismissNoticeAsync(currentUser.UserId, request.TransferId, cancellationToken);
        return outcome switch
        {
            StoreWriteResult.Written => Result<Done>.Success(Done.Value),
            StoreWriteResult.Conflict => Result<Done>.Conflict(),
            _ => Result<Done>.NotFound(),
        };
    }
}
