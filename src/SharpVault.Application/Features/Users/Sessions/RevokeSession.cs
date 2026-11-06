// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Features.Users.Sessions;

public sealed record RevokeSessionCommand(string SessionId, DateTimeOffset ExpiresAt) : IRequest<Result<SessionStatusDto>>
{
    public override string ToString() => nameof(RevokeSessionCommand);
}

public sealed class RevokeSessionCommandValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.SessionId)
            .Must(SessionRules.IsValidSessionId)
            .WithMessage(SessionRules.InvalidSessionMessage);

        RuleFor(x => x.ExpiresAt)
            .Must(expiresAt => expiresAt <= timeProvider.GetUtcNow() + SessionRules.MaxExpiryAhead)
            .WithMessage(SessionRules.ExpiryTooFarMessage);
    }
}

public sealed class RevokeSessionCommandHandler(
    ICurrentUser currentUser,
    ISessionRevocationLedger ledger,
    TimeProvider timeProvider) : IRequestHandler<RevokeSessionCommand, Result<SessionStatusDto>>
{
    public async Task<Result<SessionStatusDto>> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        if (request.ExpiresAt > timeProvider.GetUtcNow())
        {
            await ledger.RevokeAsync(currentUser.UserId, request.SessionId, request.ExpiresAt, cancellationToken);
        }

        return Result<SessionStatusDto>.Success(new SessionStatusDto { IsActive = false });
    }
}
