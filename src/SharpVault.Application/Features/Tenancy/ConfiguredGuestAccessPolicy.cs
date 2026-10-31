// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;

namespace SharpVault.Application.Features.Tenancy;

public sealed class ConfiguredGuestAccessPolicy(IOptionsMonitor<TenancyOptions> options) : IGuestAccessPolicy
{
    public ValueTask<bool> AreGuestsAllowedAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(options.CurrentValue.AllowGuests);
}
