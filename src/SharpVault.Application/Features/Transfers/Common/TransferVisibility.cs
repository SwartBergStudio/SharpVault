// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferVisibility
{
    public static bool IsHiddenFrom(TransferJob job, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.SourceUserId == actorId || job.TargetUserId == actorId;
    }
}
