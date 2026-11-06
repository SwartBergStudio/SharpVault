// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Transfers.Job;

public interface ITransferJobSignal
{
    void Signal();

    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
