// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferLifecyclePhase(ITransferStagingStore staging, AdminAuditWriter audit)
{
    public async Task ExpireAsync(TransferJobCursor cursor, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        await audit.WriteAsync(TransferAuditItems.Expired(job), cancellationToken);
        job.State = TransferState.Expired;
        await cursor.CloseAsync(cancellationToken);
    }

    public async Task CleanupAsync(TransferJobCursor cursor, CancellationToken cancellationToken)
    {
        var job = cursor.Job;
        var fence = await staging.TakeFenceAsync(job.TargetUserId, job.Id, cancellationToken);
        var staged = await staging.ListIndexAsync(job.TargetUserId, job.Id, cancellationToken);
        if (staged.Count > 0)
        {
            await staging.RemoveAsync(job.TargetUserId, job.Id, staged, cancellationToken);
        }

        job.CleanupPending = false;
        await staging.ReleaseFenceAsync(job.TargetUserId, job.Id, fence, cancellationToken);
        await cursor.CloseAsync(cancellationToken);
    }
}
