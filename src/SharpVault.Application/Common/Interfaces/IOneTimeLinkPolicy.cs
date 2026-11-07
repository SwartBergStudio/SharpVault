// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.OneTimeLinks.Common;

namespace SharpVault.Application.Common.Interfaces;

/// <summary>
/// Decides whether one-time share links can be created and how long they may live. This is the ADM-2 hook.
/// </summary>
public interface IOneTimeLinkPolicy
{
    ValueTask<OneTimeLinkSettings> GetAsync(CancellationToken cancellationToken);
}
