// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Features.Users.Sessions;

public sealed record CheckSessionQuery(string SessionId, DateTimeOffset ExpiresAt) : IRequest<Result<SessionStatusDto>>
{
    public override string ToString() => nameof(CheckSessionQuery);
}

public sealed class CheckSessionQueryValidator : AbstractValidator<CheckSessionQuery>
{
    public CheckSessionQueryValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.SessionId)
            .Must(SessionRules.IsValidSessionId)
            .WithMessage(SessionRules.InvalidSessionMessage);

        RuleFor(x => x.ExpiresAt)
            .Must(expiresAt => expiresAt <= timeProvider.GetUtcNow() + SessionRules.MaxExpiryAhead)
            .WithMessage(SessionRules.ExpiryTooFarMessage);
    }
}

public sealed class CheckSessionQueryHandler(
    ICurrentUser currentUser,
    ISessionRevocationLedger ledger,
    TimeProvider timeProvider) : IRequestHandler<CheckSessionQuery, Result<SessionStatusDto>>
{
    public async Task<Result<SessionStatusDto>> Handle(CheckSessionQuery request, CancellationToken cancellationToken)
    {
        if (request.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return Result<SessionStatusDto>.Success(new SessionStatusDto { IsActive = false });
        }

        var revoked = await ledger.IsRevokedAsync(currentUser.UserId, request.SessionId, request.ExpiresAt, cancellationToken);
        return Result<SessionStatusDto>.Success(new SessionStatusDto { IsActive = !revoked });
    }
}
