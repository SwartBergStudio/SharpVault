// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Maintenance;

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public sealed class HookedLeaseStore : IMaintenanceLeaseStore
{
    private Func<Task>? _beforeNextAcquire;

    public IMaintenanceLeaseStore Inner { get; set; } = null!;

    public Func<Task>? BeforeNextAcquire
    {
        get => Volatile.Read(ref _beforeNextAcquire);
        set => Volatile.Write(ref _beforeNextAcquire, value);
    }

    public async Task<Versioned<MaintenanceLease>?> TryAcquireAsync(string name, string holder, TimeSpan duration, CancellationToken cancellationToken)
    {
        var hook = Interlocked.Exchange(ref _beforeNextAcquire, null);
        if (hook is not null)
        {
            await hook();
        }

        return await Inner.TryAcquireAsync(name, holder, duration, cancellationToken);
    }

    public Task<Versioned<MaintenanceLease>?> RenewAsync(MaintenanceLease lease, string etag, TimeSpan duration, CancellationToken cancellationToken) =>
        Inner.RenewAsync(lease, etag, duration, cancellationToken);

    public Task ReleaseAsync(MaintenanceLease lease, string etag, CancellationToken cancellationToken) =>
        Inner.ReleaseAsync(lease, etag, cancellationToken);
}
