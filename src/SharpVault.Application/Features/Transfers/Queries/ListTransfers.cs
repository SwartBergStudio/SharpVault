// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Dtos.Transfers;

namespace SharpVault.Application.Features.Transfers.Queries;

public sealed record ListTransfersQuery(int PageSize, string? ContinuationToken) : IRequest<Result<TransferPageDto>>
{
    public const int MaxPageSize = 100;
}

public sealed class ListTransfersQueryValidator : AbstractValidator<ListTransfersQuery>
{
    private const int MinPageSize = 1;

    public ListTransfersQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(MinPageSize, ListTransfersQuery.MaxPageSize);
        RuleFor(x => x.ContinuationToken)
            .Must(ContinuationTokens.IsStorageToken)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(TransferMessages.InvalidContinuationToken);
    }
}

public sealed class ListTransfersQueryHandler(ICurrentUser currentUser, ITransferJobStore jobs)
    : IRequestHandler<ListTransfersQuery, Result<TransferPageDto>>
{
    public async Task<Result<TransferPageDto>> Handle(ListTransfersQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdmin)
        {
            return Result<TransferPageDto>.Forbidden();
        }

        TransferJobPage page;
        try
        {
            page = await jobs.ListAsync(request.PageSize, request.ContinuationToken, cancellationToken);
        }
        catch (InvalidContinuationTokenException)
        {
            return Result<TransferPageDto>.Rejected(TransferMessages.InvalidContinuationToken);
        }

        var items = page.Items
            .Select(item => item.Value)
            .Where(job => !TransferVisibility.IsHiddenFrom(job, currentUser.UserId))
            .Select(TransferDtoFactory.ToSummary)
            .ToList();

        return Result<TransferPageDto>.Success(new TransferPageDto
        {
            Items = items,
            ContinuationToken = page.ContinuationToken,
        });
    }
}
