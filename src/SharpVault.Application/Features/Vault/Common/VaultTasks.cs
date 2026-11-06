// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Vault.Common;

public static class VaultTasks
{
    public static async Task WhenAllOrDisposeAsync<TOpened>(Task<TOpened> opening, params Task[] others)
        where TOpened : IDisposable?
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(others);

        try
        {
            await Task.WhenAll([opening, .. others]);
        }
        catch
        {
            if (opening.IsCompletedSuccessfully)
            {
                (await opening)?.Dispose();
            }

            throw;
        }
    }
}
