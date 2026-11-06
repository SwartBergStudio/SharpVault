// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferLeaseLostException : Exception
{
    public TransferLeaseLostException()
        : base("The transfer job lease was lost.")
    {
    }
}
