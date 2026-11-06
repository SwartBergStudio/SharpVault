// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferStepPendingException : Exception
{
    public TransferStepPendingException()
        : base("The transfer step is not finished and will be retried.")
    {
    }
}
