// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Domain.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Commands;

public sealed record RevokeOneTimeLinkCommand(Guid LinkId) : IRequest<Result<Done>>;

public sealed class RevokeOneTimeLinkCommandValidator : AbstractValidator<RevokeOneTimeLinkCommand>
{
    public RevokeOneTimeLinkCommandValidator()
    {
        RuleFor(x => x.LinkId).NotEqual(Guid.Empty);
    }
}

public sealed class RevokeOneTimeLinkCommandHandler(
    IOneTimeLinkStore links,
    OneTimeLinkRevoker revoker,
    ICurrentUser currentUser,
    IRequestMetadata requestMetadata) : IRequestHandler<RevokeOneTimeLinkCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(RevokeOneTimeLinkCommand request, CancellationToken cancellationToken)
    {
        var entry = await links.GetIndexEntryAsync(currentUser.UserId, request.LinkId, cancellationToken);
        if (entry is null)
        {
            return Result<Done>.NotFound();
        }

        var revoked = await revoker.RevokeAsync(entry, currentUser.UserId, requestMetadata.IpAddress, cancellationToken);
        return revoked ? Result<Done>.Success(Done.Value) : Result<Done>.NotFound();
    }
}
