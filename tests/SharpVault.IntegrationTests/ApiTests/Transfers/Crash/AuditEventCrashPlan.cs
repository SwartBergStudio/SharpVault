// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Audit;

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public sealed class AuditEventCrashPlan(AuditEventType eventType)
{
    private int _armed;
    private int _fired;

    public bool Fired => Volatile.Read(ref _fired) == 1;

    public void Arm() => Volatile.Write(ref _armed, 1);

    public Task BeforeAppendAsync(object?[]? args)
    {
        if (Volatile.Read(ref _armed) == 0 || args is not [AuditEntry entry, ..] || entry.EventType != eventType)
        {
            return Task.CompletedTask;
        }

        if (Interlocked.Exchange(ref _fired, 1) == 1)
        {
            return Task.CompletedTask;
        }

        throw new SimulatedCrashException(eventType.ToString());
    }
}
