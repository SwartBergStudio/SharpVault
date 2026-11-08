// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed class ConfiguredOneTimeLinkPolicy(IOptionsMonitor<OneTimeLinkOptions> options) : IOneTimeLinkPolicy
{
    public ValueTask<OneTimeLinkSettings> GetAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new OneTimeLinkSettings(options.CurrentValue.Enabled, options.CurrentValue.MaxLifetime));
}
