// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Maintenance;

public static class MaintenanceKeys
{
    public const string PartitionKey = "lease";
    public const string RecycleBinPurgeLease = "recycle-bin-purge";
    public const string TransferJobsLease = SharpVault.Application.Features.Transfers.Job.TransferJobRunner.LeaseName;
    public const string OneTimeLinkPurgeLease = SharpVault.Application.Features.OneTimeLinks.Common.OneTimeLinkPurger.LeaseName;
}
