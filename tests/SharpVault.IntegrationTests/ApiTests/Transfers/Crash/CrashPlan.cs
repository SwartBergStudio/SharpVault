// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public sealed class CrashPlan(string point, int occurrence, bool afterEffect)
{
    private int _seen;
    private int _armed;
    private int _fired;

    public string Point { get; } = point;

    public bool Fired => Volatile.Read(ref _fired) == 1;

    public void Arm() => Volatile.Write(ref _armed, 1);

    public CrashDecision Decide(string call)
    {
        if (Volatile.Read(ref _armed) == 0 || Fired || !string.Equals(call, Point, StringComparison.Ordinal))
        {
            return CrashDecision.None;
        }

        if (Interlocked.Increment(ref _seen) != occurrence)
        {
            return CrashDecision.None;
        }

        Volatile.Write(ref _fired, 1);
        return afterEffect ? CrashDecision.After : CrashDecision.Before;
    }
}
