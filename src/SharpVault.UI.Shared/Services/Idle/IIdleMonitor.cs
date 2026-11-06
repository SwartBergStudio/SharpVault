// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Idle;

public interface IIdleMonitor : IAsyncDisposable
{
    Task StartAsync(string signOutFormId, TimeSpan timeout, string? sessionTag, CancellationToken cancellationToken);

    Task StopAsync();
}
