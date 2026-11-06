// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferJobChangedException : Exception
{
    public TransferJobChangedException()
        : base("The transfer job was changed by another request.")
    {
    }
}
