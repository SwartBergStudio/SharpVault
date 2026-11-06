// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferPublishCollisionException : Exception
{
    public TransferPublishCollisionException()
        : base("A transferred secret id already exists in the target vault.")
    {
    }
}
