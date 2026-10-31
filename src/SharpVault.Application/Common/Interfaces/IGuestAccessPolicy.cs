// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Interfaces;

/// <summary>
/// Decides whether Entra guest accounts may use SharpVault. This is the ADM-1 hook.
/// </summary>
public interface IGuestAccessPolicy
{
    ValueTask<bool> AreGuestsAllowedAsync(CancellationToken cancellationToken);
}
