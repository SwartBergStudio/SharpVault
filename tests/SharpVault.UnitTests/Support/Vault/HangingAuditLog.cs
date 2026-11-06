// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class HangingAuditLog : IAuditLog
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
