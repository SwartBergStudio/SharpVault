// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed class TransferJobCursor(ITransferJobStore jobs, Versioned<TransferJob> current)
{
    private Versioned<TransferJob> _current = current;

    public TransferJob Job => _current.Value;

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var saved = await jobs.ReplaceAsync(_current.Value, _current.ETag, cancellationToken);
        _current = saved ?? throw new TransferJobChangedException();
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        var closed = await jobs.CloseAsync(_current.Value, _current.ETag, cancellationToken);
        if (closed != StoreWriteResult.Written)
        {
            throw new TransferJobChangedException();
        }
    }
}
