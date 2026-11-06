// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class RecordingAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => [.. _entries];

    public Exception? Failure { get; set; }

    public Func<AuditEntry, Exception?>? FailWhen { get; set; }

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        var failure = FailWhen?.Invoke(entry) ?? Failure;
        if (failure is not null)
        {
            return Task.FromException(failure);
        }

        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}
