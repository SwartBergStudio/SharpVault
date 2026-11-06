// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Maintenance;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferLeaseKeeper(
    IMaintenanceLeaseStore leases,
    TimeProvider timeProvider,
    TimeSpan duration,
    Versioned<MaintenanceLease> lease)
{
    private const int RenewDivisor = 3;

    private Versioned<MaintenanceLease> _lease = lease;
    private DateTimeOffset _renewedAt = timeProvider.GetUtcNow();

    public async Task RenewIfDueAsync(CancellationToken cancellationToken)
    {
        var elapsed = timeProvider.GetUtcNow() - _renewedAt;
        if (elapsed >= duration)
        {
            throw new TransferLeaseLostException();
        }

        if (elapsed < duration / RenewDivisor)
        {
            return;
        }

        var renewed = await leases.RenewAsync(_lease.Value, _lease.ETag, duration, cancellationToken);
        if (renewed is null)
        {
            throw new TransferLeaseLostException();
        }

        _lease = renewed;
        _renewedAt = timeProvider.GetUtcNow();
    }

    public Task ReleaseAsync(CancellationToken cancellationToken) =>
        leases.ReleaseAsync(_lease.Value, _lease.ETag, cancellationToken);
}
