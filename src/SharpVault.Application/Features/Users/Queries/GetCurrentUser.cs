// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Dtos;

namespace SharpVault.Application.Features.Users.Queries;

public sealed record GetCurrentUserQuery : IRequest<Result<CurrentUserDto>>;

public sealed class GetCurrentUserQueryHandler(ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, Result<CurrentUserDto>>
{
    public Task<Result<CurrentUserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var dto = new CurrentUserDto(currentUser.UserId, currentUser.DisplayName, currentUser.IsAdmin);
        return Task.FromResult(Result<CurrentUserDto>.Success(dto));
    }
}
