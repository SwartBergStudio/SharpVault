// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Queries;

public sealed record GetOneTimeLinkSettingsQuery : IRequest<Result<OneTimeLinkSettingsDto>>;

public sealed class GetOneTimeLinkSettingsQueryHandler(IOneTimeLinkPolicy policy)
    : IRequestHandler<GetOneTimeLinkSettingsQuery, Result<OneTimeLinkSettingsDto>>
{
    public async Task<Result<OneTimeLinkSettingsDto>> Handle(GetOneTimeLinkSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await policy.GetAsync(cancellationToken);
        var defaultLifetime = settings.MaxLifetime < OneTimeLinkLimits.DefaultLifetime ? settings.MaxLifetime : OneTimeLinkLimits.DefaultLifetime;

        return Result<OneTimeLinkSettingsDto>.Success(new OneTimeLinkSettingsDto
        {
            Enabled = settings.Enabled,
            DefaultLifetimeMinutes = (int)defaultLifetime.TotalMinutes,
            MaxLifetimeMinutes = (int)settings.MaxLifetime.TotalMinutes,
        });
    }
}
