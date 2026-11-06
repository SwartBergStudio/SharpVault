// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Maintenance;

namespace SharpVault.Application.Common.Interfaces;

public interface IMaintenanceLeaseStore
{
    Task<Versioned<MaintenanceLease>?> TryAcquireAsync(string name, string holder, TimeSpan duration, CancellationToken cancellationToken);

    Task<Versioned<MaintenanceLease>?> RenewAsync(MaintenanceLease lease, string etag, TimeSpan duration, CancellationToken cancellationToken);

    Task ReleaseAsync(MaintenanceLease lease, string etag, CancellationToken cancellationToken);
}
