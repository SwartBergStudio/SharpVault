// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;

namespace SharpVault.Application.Features.OneTimeLinks.Commands;

public sealed record DismissOneTimeLinkNoticeCommand(string NoticeId) : IRequest<Result<Done>>;

public sealed partial class DismissOneTimeLinkNoticeCommandValidator : AbstractValidator<DismissOneTimeLinkNoticeCommand>
{
    private const string NoticeIdPattern = @"^n_[0-9]{19}_[0-9a-f]{32}\z";

    public DismissOneTimeLinkNoticeCommandValidator()
    {
        RuleFor(x => x.NoticeId)
            .Must(IsNoticeId)
            .WithMessage(OneTimeLinkMessages.InvalidNoticeId);
    }

    private static bool IsNoticeId(string? noticeId) => noticeId is not null && NoticeIdRegex().IsMatch(noticeId);

    [GeneratedRegex(NoticeIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex NoticeIdRegex();
}

public sealed class DismissOneTimeLinkNoticeCommandHandler(IOneTimeLinkStore links, ICurrentUser currentUser)
    : IRequestHandler<DismissOneTimeLinkNoticeCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(DismissOneTimeLinkNoticeCommand request, CancellationToken cancellationToken)
    {
        var outcome = await links.DismissNoticeAsync(currentUser.UserId, request.NoticeId, cancellationToken);
        return outcome switch
        {
            StoreWriteResult.Written => Result<Done>.Success(Done.Value),
            StoreWriteResult.Conflict => Result<Done>.Conflict(),
            _ => Result<Done>.NotFound(),
        };
    }
}
